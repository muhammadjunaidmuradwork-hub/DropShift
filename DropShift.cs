using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DropShift
{
    static class Program
    {
        internal static bool Diagnostics, Background;
        [STAThread]
        static int Main(string[] args)
        {
            Diagnostics = args.Contains("--diagnostics");
            Background = args.Contains("--background");
            if(args.Contains("--enable-startup")) StartupSettings.SetEnabled(true);
            if (args.Length == 2 && args[0] == "--self-test") return Checks.Run(args[1]);
            bool first;
            using (var mutex = new Mutex(true, "Local\\DropShift.Prototype", out first))
            {
                if (!first) { if(!Background) MessageBox.Show("DropShift is already running. Open it from the notification area.", "DropShift"); return 0; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Home());
                OfficeWorker.Stop();
            }
            return 0;
        }
    }

    static class StartupSettings
    {
        const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";
        static string Command {get{return "\""+Application.ExecutablePath+"\" --background";}}
        public static bool Enabled {get{using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(KeyPath))return key!=null && String.Equals(key.GetValue("DropShift") as string,Command,StringComparison.OrdinalIgnoreCase);}}
        public static void SetEnabled(bool enabled)
        {
            using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(KeyPath)){
                if(enabled)key.SetValue("DropShift",Command,Microsoft.Win32.RegistryValueKind.String);
                else key.DeleteValue("DropShift",false);
            }
            Log.Write("Start with Windows "+(enabled?"enabled":"disabled"));
        }
    }

    static class Log
    {
        static readonly object Gate = new object();
        public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropShift");
        public static void Write(string text)
        {
            try { lock (Gate) { Directory.CreateDirectory(Folder); File.AppendAllText(Path.Combine(Folder, "activity.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); } }
            catch { /* Logging must never interrupt a drag. */ }
        }
    }

    static class Converter
    {
        public static string ConvertPng(string source) { return ImageConverter.Convert(source,"JPG"); }

        internal static void ValidatePng(Stream stream)
        {
            byte[] signature = {137,80,78,71,13,10,26,10};
            foreach (byte b in signature) if (stream.ReadByte() != b) throw new InvalidDataException("The file does not have a valid PNG signature.");
            byte[] header = new byte[8];
            bool first = true, end = false;
            while (stream.Position < stream.Length)
            {
                if (stream.Read(header, 0, 8) != 8) throw new InvalidDataException("Truncated PNG chunk.");
                uint length = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
                string type = Encoding.ASCII.GetString(header, 4, 4);
                if (first && (type != "IHDR" || length != 13)) throw new InvalidDataException("Missing PNG image header.");
                first = false;
                if (type == "acTL") throw new InvalidDataException("Animated PNG is not supported in this prototype.");
                if (length > stream.Length - stream.Position - 4) throw new InvalidDataException("Truncated PNG data.");
                stream.Seek((long)length + 4, SeekOrigin.Current);
                if (type == "IEND") { end = true; break; }
            }
            if (!end) throw new InvalidDataException("Incomplete PNG file.");
        }

        internal static void ApplyOrientation(Image image)
        {
            if (!image.PropertyIdList.Contains(0x112)) return;
            byte[] value = image.GetPropertyItem(0x112).Value;
            if (value.Length < 2) return;
            int orientation = BitConverter.ToUInt16(value, 0);
            RotateFlipType[] transforms = { RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipNone, RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate90FlipX, RotateFlipType.Rotate90FlipNone, RotateFlipType.Rotate270FlipX, RotateFlipType.Rotate270FlipNone };
            if (orientation >= 2 && orientation <= 8) image.RotateFlip(transforms[orientation]);
        }
    }

    static class Native
    {
        public delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int type, Hook callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr handle);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr handle, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet=CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] public struct MouseData { public Point Position; public uint Mouse, Flags, Time; public UIntPtr Extra; }
    }

    sealed class Gesture : IDisposable
    {
        readonly Control dispatcher;
        readonly Action<Point> activate;
        readonly Action cancel;
        readonly Native.Hook mouseCallback, keyCallback;
        IntPtr mouse, keyboard;
        readonly Thread hookThread;
        uint hookThreadId;
        Point start, current;
        bool down, moved, shiftHeld;
        public Gesture(Control dispatcher, Action<Point> activate, Action cancel)
        {
            this.dispatcher = dispatcher; this.activate = activate; this.cancel = cancel;
            mouseCallback = Mouse; keyCallback = Key;
            // The UI/OLE thread may block in native dialogs. A separate message
            // loop keeps global input callbacks below Windows' hook timeout.
            Exception failure = null;
            using (var ready = new ManualResetEvent(false))
            {
                hookThread = new Thread(delegate() {
                    hookThreadId = Native.GetCurrentThreadId();
                    IntPtr module = Native.GetModuleHandle(null);
                    mouse = Native.SetWindowsHookEx(14, mouseCallback, module, 0);
                    keyboard = Native.SetWindowsHookEx(13, keyCallback, module, 0);
                    if (mouse == IntPtr.Zero || keyboard == IntPtr.Zero) failure = new InvalidOperationException("Windows could not register the activation gesture. Use Choose images instead.");
                    ready.Set();
                    try { if (failure == null) Application.Run(); }
                    finally { if (mouse != IntPtr.Zero) Native.UnhookWindowsHookEx(mouse); if (keyboard != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboard); }
                });
                hookThread.IsBackground = true; hookThread.Name = "DropShift input"; hookThread.SetApartmentState(ApartmentState.STA); hookThread.Start(); ready.WaitOne();
            }
            if (failure != null) throw failure;
        }
        void Post(Action action) { if (!dispatcher.IsDisposed) { try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { } } }
        IntPtr Mouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int msg = message.ToInt32();
                var info = (Native.MouseData)Marshal.PtrToStructure(data, typeof(Native.MouseData));
                current = info.Position;
                if (msg == 0x201) { down = true; moved = false; start = current; if (Program.Diagnostics) Post(delegate { Log.Write("Input: left down"); }); }
                if (msg == 0x200 && down) moved = moved || Math.Abs(current.X - start.X) > SystemInformation.DragSize.Width || Math.Abs(current.Y - start.Y) > SystemInformation.DragSize.Height;
                if (msg == 0x202) down = false;
            }
            return Native.CallNextHookEx(mouse, code, message, data);
        }
        IntPtr Key(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int key = Marshal.ReadInt32(data), msg = message.ToInt32();
                bool pressed = msg == 0x100 || msg == 0x104;
                if (key == 0x10 || key == 0xA0 || key == 0xA1)
                {
                    if (Program.Diagnostics && pressed) { string state = "Shift down; mouse=" + down + "; moved=" + moved; Post(delegate { Log.Write("Input: " + state); }); }
                    if (pressed && !shiftHeld && down && moved) { Point p = current; Post(delegate { activate(p); }); }
                    shiftHeld = pressed;
                }
                if (pressed && key == 0x1B) Post(cancel);
            }
            return Native.CallNextHookEx(keyboard, code, message, data);
        }
        public void Dispose() { if (hookThread != null && hookThread.IsAlive) { Native.PostThreadMessage(hookThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero); hookThread.Join(1500); } }
    }

    sealed class Wheel : Form
    {
        readonly Action<string[],string> convert;
        readonly System.Windows.Forms.Timer release = new System.Windows.Forms.Timer();
        string[] files;
        string message = "Drag a file";
        string[] targets = new string[0];
        int selected = -1;
        bool hot;
        int released;
        public Wheel(Action<string[],string> convert)
        {
            this.convert = convert;
            Text = "DropShift conversion wheel";
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            AutoScaleMode = AutoScaleMode.None; ClientSize = new Size(360,360); BackColor = Theme.Paper;
            DoubleBuffered = true; AllowDrop = true;
            using (var path = new GraphicsPath()) { path.AddEllipse(2,2,356,356); Region = new Region(path); }
            DragEnter += EnterDrag; DragOver += OverDrag;
            DragLeave += delegate { hot = false; Invalidate(); };
            DragDrop += DropFiles;
            release.Interval = 80;
            release.Tick += delegate {
                if ((Native.GetAsyncKeyState(1) & 0x8000) == 0) { if (++released >= 3) Dismiss(); } else released = 0;
            };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080; return cp; } }
        protected override void WndProc(ref Message m) { if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } base.WndProc(ref m); }
        public void Present(Point point)
        {
            files = null; message = "Drag a file"; targets = new string[0]; selected = -1; hot = false; released = 0;
            Rectangle area = Screen.FromPoint(point).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(point.X - Width / 2, area.Right - Width)), Math.Max(area.Top, Math.Min(point.Y - Height / 2, area.Bottom - Height)));
            Show(); Native.SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x0010 | 0x0040);
            release.Start(); Invalidate(); Log.Write("Wheel opened");
        }
        public void Dismiss() { release.Stop(); Hide(); files = null; hot = false; }
        int TargetAt(Point point)
        {
            return Theme.Hit(point,targets.Length);
        }
        void EnterDrag(object sender, DragEventArgs e)
        {
            files = null;
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var candidate = e.Data.GetData(DataFormats.FileDrop) as string[];
                    if (candidate != null && candidate.Length > 0 && candidate.Length <= 100) files = candidate;
                }
            }
            catch { }
            targets = Formats.Targets(files);
            message = targets.Length==0 ? "No compatible outputs" : files.Length + " file(s) ready";
            Log.Write("DragEnter: " + message + (files==null ? "" : " ["+String.Join(", ",files.Select(Path.GetExtension))+"]"));
            OverDrag(sender, e);
        }
        void OverDrag(object sender, DragEventArgs e)
        {
            selected = TargetAt(PointToClient(new Point(e.X,e.Y)));
            bool next = files != null && (e.AllowedEffect & DragDropEffects.Copy) != 0 && selected >= 0;
            e.Effect = next ? DragDropEffects.Copy : DragDropEffects.None;
            if (hot != next) hot = next;
            Invalidate();
        }
        void DropFiles(object sender, DragEventArgs e)
        {
            OverDrag(sender,e);
            var accepted = e.Effect == DragDropEffects.Copy ? files : null;
            string target = accepted == null ? null : targets[selected];
            Dismiss();
            if (accepted != null) { Log.Write(target + " drop accepted (COPY)"); BeginInvoke(new Action(delegate { convert(accepted,target); })); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            string center=hot ? targets[selected] : files==null ? "DropShift" : Formats.Format(files[0]) ?? "Unsupported";
            string detail=hot ? "Release to convert" : files==null ? "Drag a file here" : targets.Length==0 ? (files.Length>1 ? "No shared output\nEsc to cancel" : "No output for "+Path.GetExtension(files[0])+"\nEsc to cancel") : "Center to cancel\nOriginal kept";
            Theme.Ring(e.Graphics,targets,hot ? selected : -1,center,detail);
        }
        protected override void Dispose(bool disposing) { if(disposing) release.Dispose(); base.Dispose(disposing); }
    }

    sealed class Home : Form
    {
        readonly NotifyIcon tray;
        readonly Wheel wheel;
        Gesture gesture;
        readonly TextBox history;
        readonly Label state;
        readonly Queue<Tuple<string,string>> pending = new Queue<Tuple<string,string>>();
        bool running, exiting;
        string latest;
        public Home()
        {
            Text="DropShift 0.3.2.1"; BackColor=Theme.Paper; ForeColor=Theme.Ink;
            if(Program.Background)Opacity=0;
            Font=new Font("Segoe UI",10); ClientSize=new Size(820,650); FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen; DoubleBuffered=true;
            LabelAt("D R O P S H I F T",36,28,500,30,12,Theme.Accent,FontStyle.Bold);
            LabelAt("A little shift.\nA new format.",34,83,480,123,32,Theme.Ink,FontStyle.Bold);
            LabelAt("Your files, transformed right where they are.",37,216,460,28,11,Theme.Muted,FontStyle.Regular);
            Controls.Add(new WheelPreview { Left=545,Top=42,Width=220,Height=220 });
            LabelAt("01   DRAG A FILE",38,280,245,27,10,Theme.Accent,FontStyle.Bold);
            LabelAt("02   TAP SHIFT",294,280,245,27,10,Theme.Accent,FontStyle.Bold);
            LabelAt("03   DROP ON A FORMAT",550,280,250,27,10,Theme.Accent,FontStyle.Bold);
            LabelAt("Keep the mouse held.",38,309,245,26,10,Theme.Muted,FontStyle.Regular);
            LabelAt("The wheel meets your cursor.",294,309,245,26,10,Theme.Muted,FontStyle.Regular);
            LabelAt("A new copy. Original untouched.",550,309,250,26,10,Theme.Muted,FontStyle.Regular);
            ButtonAt("Choose files…",36,365,230,delegate { Choose(); });
            ButtonAt("Try sample files",282,365,230,delegate { OpenSamples(); });
            ButtonAt("Show latest result",528,365,252,delegate { if(latest!=null) { if(Directory.Exists(latest)) Process.Start(new ProcessStartInfo(latest){UseShellExecute=true}); else Process.Start("explorer.exe","/select,\""+latest+"\""); } });
            state=LabelAt("Ready  ·  Offline conversion  ·  Originals always kept",38,435,740,28,10,Theme.Accent,FontStyle.Bold);
            history=new TextBox { Left=38,Top=481,Width=740,Height=112,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=Color.FromArgb(247,238,224),ForeColor=Theme.Ink,BorderStyle=BorderStyle.None,Text="Your converted files will appear here." };
            Controls.Add(history);
            LabelAt("Close this window to keep DropShift in the tray.   •   Escape cancels a drag.",38,613,750,24,9,Theme.Muted,FontStyle.Regular);
            var menu=new ContextMenuStrip(); menu.Items.Add("Open DropShift",null,delegate { Show(); WindowState=FormWindowState.Normal; Activate(); }); menu.Items.Add("Choose files…",null,delegate { Choose(); }); menu.Items.Add("Exit DropShift",null,delegate { Exit(); });
            var startup=new ToolStripMenuItem("Start with Windows");
            menu.Items.Insert(2,startup);
            menu.Opening+=delegate {try{startup.Checked=StartupSettings.Enabled;}catch(Exception ex){Log.Write("Startup setting: "+ex.Message);}};
            startup.Click+=delegate {try{StartupSettings.SetEnabled(!StartupSettings.Enabled);}catch(Exception ex){MessageBox.Show(ex.Message,"Could not change startup setting");}};
            tray=new NotifyIcon { Icon=SystemIcons.Application,Text="DropShift — drag a file, then tap Shift",ContextMenuStrip=menu,Visible=true };
            tray.DoubleClick+=delegate { Show(); WindowState=FormWindowState.Normal; Activate(); };
            wheel=new Wheel(QueueFiles);
            Formats.Progress=delegate(string message){if(!IsDisposed&&IsHandleCreated)try{BeginInvoke((Action)delegate{if(running)state.Text=message;});}catch(InvalidOperationException){}};
            Shown+=delegate {
                if(Program.Background){Hide();Opacity=1;Log.Write("Background mode active; home window hidden");}
                OfficeWorker.WarmUp();
                try { gesture=new Gesture(this,delegate(Point p) { wheel.Present(p); },delegate { wheel.Dismiss(); }); Log.Write("App started; input hooks registered"); }
                catch(Exception ex) { state.Text=ex.Message; Log.Write(ex.Message); }
            };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(!exiting && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
        }
        Label LabelAt(string text,int x,int y,int width,int height,float size,Color color,FontStyle style)
        {
            var label=new Label { Text=text,Left=x,Top=y,Width=width,Height=height,Font=new Font("Segoe UI",size,style),ForeColor=color }; Controls.Add(label); return label;
        }
        void ButtonAt(string text,int x,int y,int width,Action action)
        {
            var button=new WarmButton { Text=text,Left=x,Top=y,Width=width,Height=48,Primary=x==36,BackColor=Theme.Paper };
            button.Click+=delegate { action(); }; Controls.Add(button);
        }
        void Choose()
        {
            using(var picker=new OpenFileDialog { Filter="Supported files|*.png;*.jpg;*.jpeg;*.webp;*.pdf;*.bmp;*.tif;*.tiff;*.gif;*.txt;*.doc;*.docx;*.docm;*.odt;*.rtf;*.xls;*.xlsx;*.xlsm;*.csv;*.tsv;*.ods;*.ppt;*.pptx;*.pptm;*.odp;*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.wma;*.opus;*.aiff;*.mp4;*.mov;*.mkv;*.avi;*.webm;*.m4v;*.wmv;*.mpeg;*.mpg|All files|*.*",Multiselect=true,Title="Choose files to convert safely" }) {
                if(picker.ShowDialog(this)!=DialogResult.OK) return;
                var formats=Formats.Targets(picker.FileNames);
                if(formats.Length==0) { MessageBox.Show("Select files with a shared output format.","DropShift"); return; }
                using(var dialog=new Form { Text="Convert to",ClientSize=new Size(520,230),BackColor=Theme.Paper,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false }) {
                    for(int i=0;i<formats.Length;i++) { string target=formats[i]; var button=new WarmButton { Text=target,Left=20+(i%3)*165,Top=25+(i/3)*60,Width=150,Height=45 }; button.Click+=delegate { dialog.Close(); QueueFiles(picker.FileNames,target); }; dialog.Controls.Add(button); }
                    dialog.ShowDialog(this);
                }
            }
        }
        void OpenSamples()
        {
            try
            {
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Samples");
                Directory.CreateDirectory(folder);
                string path=Path.Combine(folder,"Try me.png");
                if(!File.Exists(path)) Checks.MakePng(path);
                // Let the shell resolve the directory itself, rather than parsing
                // a quoted argument passed to an existing Explorer process.
                Process.Start(new ProcessStartInfo { FileName=folder, UseShellExecute=true, Verb="open" });
                history.AppendText(Environment.NewLine+"Sample folder: "+folder);
                Log.Write("Opened sample folder: "+folder);
            }
            catch(Exception ex) { MessageBox.Show(ex.Message,"Could not open samples"); }
        }
        async void QueueFiles(string[] files,string target)
        {
            if(pending.Count+files.Length>100) { MessageBox.Show("The queue is full. Please wait for current conversions to finish.","DropShift"); return; }
            foreach(string file in files) pending.Enqueue(Tuple.Create(file,target));
            if(running) return;
            running=true;
            try
            {
                while(pending.Count>0)
                {
                    var job=pending.Dequeue(); string file=job.Item1; state.Text="Converting " + Path.GetFileName(file) + "…";
                    try {
                        var completed=await Task.Run(delegate { string output=Formats.Convert(file,job.Item2);return Tuple.Create(output,Formats.Notice); });
                        string result=completed.Item1;
                        latest=result; history.AppendText(Environment.NewLine+"Saved: "+result); Log.Write("Saved "+result); tray.ShowBalloonTip(3500,job.Item2+" saved · original kept",Path.GetFileName(result),ToolTipIcon.Info);
                        if(!String.IsNullOrEmpty(completed.Item2))history.AppendText(Environment.NewLine+completed.Item2);
                    }
                    catch(Exception ex) { string error=Path.GetFileName(file)+": "+ex.Message; history.AppendText(Environment.NewLine+"Failed: "+error); Log.Write("Failed "+error); tray.ShowBalloonTip(5000,"Conversion failed · original kept",error,ToolTipIcon.Error); }
                }
                state.Text="Ready · originals kept, results listed below";
            }
            finally { running=false; }
        }
        void Exit()
        {
            if(running) { MessageBox.Show("Please let the current conversion queue finish before exiting.","DropShift"); return; }
            exiting=true; Close();
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) { if(gesture!=null) gesture.Dispose(); wheel.Dispose(); tray.Visible=false; tray.Dispose(); } base.Dispose(disposing);
        }
    }

    static class Checks
    {
        public static void MakePng(string path)
        {
            using(var bitmap=new Bitmap(480,320,PixelFormat.Format32bppArgb))
            using(var graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                using(var brush=new LinearGradientBrush(new Rectangle(35,35,410,250),Color.FromArgb(90,209,165),Color.FromArgb(66,118,234),30)) graphics.FillEllipse(brush,35,35,410,250);
                using(var font=new Font("Segoe UI",32,FontStyle.Bold)) graphics.DrawString("DropShift",font,Brushes.White,86,125);
                bitmap.Save(path,ImageFormat.Png);
            }
        }
        static string Hash(string path) { using(var stream=File.OpenRead(path)) using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)); }
        static void Assert(bool condition,string message) { if(!condition) throw new Exception(message); }
        public static int Run(string folder)
        {
            Directory.CreateDirectory(folder); var results=new List<string>();
            try
            {
                string source=Path.Combine(folder,"Transparency sample.png"); MakePng(source); string originalHash=Hash(source);
                string jpg=Converter.ConvertPng(source);
                Assert(Hash(source)==originalHash,"Original bytes changed"); results.Add("PASS: original SHA-256 unchanged after conversion");
                using(var image=new Bitmap(jpg)) { Assert(image.RawFormat.Guid==ImageFormat.Jpeg.Guid && image.Width==480 && image.Height==320,"Invalid JPEG output"); Color p=image.GetPixel(0,0); Assert(p.R>248 && p.G>248 && p.B>248,"Transparency not flattened to white"); }
                results.Add("PASS: real 480x320 JPEG; transparent pixels become white");
                string firstHash=Hash(jpg),second=Converter.ConvertPng(source);
                Assert(jpg!=second && Hash(jpg)==firstHash && Hash(source)==originalHash,"Collision overwrote a file"); results.Add("PASS: repeated conversion creates unique output and preserves earlier JPG");
                string fake=Path.Combine(folder,"Invalid.png"); File.WriteAllText(fake,"not a PNG"); string fakeHash=Hash(fake); bool rejected=false;
                try { Converter.ConvertPng(fake); } catch(InvalidDataException) { rejected=true; }
                Assert(rejected && Hash(fake)==fakeHash,"Malformed input was not safely rejected"); results.Add("PASS: malformed PNG rejected with source unchanged");
                File.SetAttributes(source,FileAttributes.ReadOnly);
                try { Converter.ConvertPng(source); Assert(Hash(source)==originalHash,"Read-only input changed"); } finally { File.SetAttributes(source,FileAttributes.Normal); }
                results.Add("PASS: read-only source converts without modifying it");
                Assert(Directory.GetFiles(folder,".dropshift-*.tmp").Length==0,"Temporary files leaked"); results.Add("PASS: no leftover conversion temporary files");
                File.WriteAllLines(Path.Combine(folder,"test-results.txt"),results); return 0;
            }
            catch(Exception ex) { results.Add("FAIL: "+ex); File.WriteAllLines(Path.Combine(folder,"test-results.txt"),results); return 1; }
        }
    }
}
