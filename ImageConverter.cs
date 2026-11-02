using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DropShift
{
    static class ImageConverter
    {
        public static string Format(string path) { string e=Path.GetExtension(path).ToLowerInvariant(); return e==".jpg" || e==".jpeg" ? "JPG" : e==".png" ? "PNG" : e==".webp" ? "WebP" : e==".bmp" ? "BMP" : e==".tif" || e==".tiff" ? "TIFF" : e==".gif" ? "GIF" : null; }
        public static string[] Targets(string[] files) {
            if(files==null || files.Length==0 || files.Length>100 || files.Any(p=>Format(p)==null)) return new string[0];
            return new[]{"JPG","PNG","WebP","BMP","TIFF"}.Where(f=>files.All(p=>Format(p)!=f)).ToArray();
        }
        static ImageFormat NativeFormat(string f) { return f=="JPG"?ImageFormat.Jpeg:f=="PNG"?ImageFormat.Png:f=="BMP"?ImageFormat.Bmp:f=="TIFF"?ImageFormat.Tiff:ImageFormat.Gif; }
        static string Quote(string path) { return "\""+path+"\""; }
        static void Run(string tool,string args) {
            string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Codecs",tool+".exe");
            if(!File.Exists(exe)) throw new IOException("WebP tools are missing. Extract the entire portable ZIP, including Codecs.");
            using(var p=new Process { StartInfo=new ProcessStartInfo(exe,args) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true } }) {
                p.Start(); var err=p.StandardError.ReadToEndAsync(); var output=p.StandardOutput.ReadToEndAsync();
                if(!p.WaitForExit(60000)) { p.Kill(); p.WaitForExit(); throw new IOException("WebP conversion timed out."); }
                Task.WaitAll(err,output); if(p.ExitCode!=0) throw new InvalidDataException("WebP codec rejected the image: "+err.Result.Trim());
            }
        }
        static void ValidateWebp(Stream s) {
            using(var r=new BinaryReader(s,Encoding.ASCII,true)) {
                if(new string(r.ReadChars(4))!="RIFF") throw new InvalidDataException("Invalid WebP signature.");
                uint size=r.ReadUInt32(); if((long)size+8!=s.Length || new string(r.ReadChars(4))!="WEBP") throw new InvalidDataException("Incomplete WebP file.");
                while(s.Position<s.Length) {
                    if(s.Length-s.Position<8) throw new InvalidDataException("Truncated WebP chunk.");
                    string tag=new string(r.ReadChars(4)); uint len=r.ReadUInt32(); long end=s.Position+len+(len&1);
                    if(end>s.Length) throw new InvalidDataException("Truncated WebP data.");
                    if(tag=="ANIM" || tag=="ANMF") throw new InvalidDataException("Animated WebP is not supported.");
                    s.Position=end;
                }
            }
        }
        public static string Convert(string source,string target) {
            source=Path.GetFullPath(source);
            if(!Targets(new[]{source}).Contains(target)) throw new InvalidDataException("Choose a different supported output format.");
            string folder=Path.GetDirectoryName(source),prefix=Path.Combine(folder,".dropshift-"+Guid.NewGuid().ToString("N"));
            string temp=prefix+".tmp",decoded=prefix+"-decoded.png",canonical=prefix+"-input.png",verified=prefix+"-verify.png";
            try {
                using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    string format=Format(source);
                    if(format=="PNG") Converter.ValidatePng(input);
                    if(format=="WebP") { ValidateWebp(input); Run("dwebp",Quote(source)+" -o "+Quote(decoded)); }
                    input.Position=0;
                    using(var image=format=="WebP" ? Image.FromFile(decoded) : Image.FromStream(input,true,true)) {
                        if(format!="WebP" && image.RawFormat.Guid!=NativeFormat(format).Guid) throw new InvalidDataException("Image content does not match its extension.");
                        if((long)image.Width*image.Height>50000000) throw new InvalidDataException("Image exceeds the 50 megapixel limit.");
                        if(image.FrameDimensionsList.Any(d=>image.GetFrameCount(new FrameDimension(d))>1)) throw new InvalidDataException("Animated or multi-frame images are not supported; originals are kept.");
                        Converter.ApplyOrientation(image);
                        using(var bitmap=new Bitmap(image.Width,image.Height,target=="JPG" || target=="BMP" ? PixelFormat.Format24bppRgb : PixelFormat.Format32bppArgb)) {
                            using(var g=Graphics.FromImage(bitmap)) { g.Clear(target=="JPG" || target=="BMP" ? Color.White : Color.Transparent); g.CompositingMode=target=="JPG" || target=="BMP" ? CompositingMode.SourceOver : CompositingMode.SourceCopy; g.DrawImage(image,new Rectangle(0,0,bitmap.Width,bitmap.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel); }
                            if(target=="WebP") { bitmap.Save(canonical,ImageFormat.Png); Run("cwebp","-quiet -lossless -exact "+Quote(canonical)+" -o "+Quote(temp)); Run("dwebp",Quote(temp)+" -o "+Quote(verified)); }
                            else using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                                if(target=="JPG") using(var settings=new EncoderParameters(1)) { settings.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,92L); bitmap.Save(stream,ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==ImageFormat.Jpeg.Guid),settings); }
                                else bitmap.Save(stream,NativeFormat(target));
                                stream.Flush(true);
                            }
                            using(var check=Image.FromFile(target=="WebP" ? verified : temp)) {
                                if(check.Width!=bitmap.Width || check.Height!=bitmap.Height || check.RawFormat.Guid!=(target=="WebP" ? ImageFormat.Png.Guid : NativeFormat(target).Guid)) throw new InvalidDataException("Output validation failed.");
                            }
                        }
                    }
                }
                string stem=Path.GetFileNameWithoutExtension(source)+" (converted)";
                for(int n=0;n<10000;n++) {
                    string dest=Path.Combine(folder,stem+(n==0 ? "" : " "+(n+1))+"."+target.ToLowerInvariant());
                    try { File.Move(temp,dest); return dest; } catch(IOException) { if(!File.Exists(dest)) throw; }
                }
                throw new IOException("No free output filename.");
            }
            finally { foreach(string path in new[]{temp,decoded,canonical,verified}) if(File.Exists(path)) try { File.Delete(path); } catch { } }
        }
    }
}
