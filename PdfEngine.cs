using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace DropShift {
 static class PdfEngine {
  const string Dll="pdfium.dll";
  [DllImport(Dll)] static extern void FPDF_InitLibrary();
  [DllImport(Dll)] static extern IntPtr FPDF_LoadMemDocument64(byte[] data,UIntPtr length,IntPtr password);
  [DllImport(Dll)] static extern void FPDF_CloseDocument(IntPtr doc);
  [DllImport(Dll)] static extern int FPDF_GetPageCount(IntPtr doc);
  [DllImport(Dll)] static extern IntPtr FPDF_LoadPage(IntPtr doc,int index);
  [DllImport(Dll)] static extern void FPDF_ClosePage(IntPtr page);
  [DllImport(Dll)] static extern double FPDF_GetPageWidth(IntPtr page);
  [DllImport(Dll)] static extern double FPDF_GetPageHeight(IntPtr page);
  [DllImport(Dll)] static extern IntPtr FPDFBitmap_Create(int width,int height,int alpha);
  [DllImport(Dll)] static extern void FPDFBitmap_Destroy(IntPtr bitmap);
  [DllImport(Dll)] static extern void FPDFBitmap_FillRect(IntPtr bitmap,int left,int top,int width,int height,uint color);
  [DllImport(Dll)] static extern void FPDF_RenderPageBitmap(IntPtr bitmap,IntPtr page,int x,int y,int width,int height,int rotate,int flags);
  [DllImport(Dll)] static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
  [DllImport(Dll)] static extern int FPDFBitmap_GetStride(IntPtr bitmap);
  [DllImport(Dll)] static extern IntPtr FPDFText_LoadPage(IntPtr page);
  [DllImport(Dll)] static extern void FPDFText_ClosePage(IntPtr page);
  [DllImport(Dll)] static extern int FPDFText_CountChars(IntPtr page);
  [DllImport(Dll)] static extern int FPDFText_GetText(IntPtr page,int start,int count,[Out] ushort[] text);
  [DllImport(Dll)] static extern int FPDFPage_CountObjects(IntPtr page);
  [DllImport(Dll)] static extern IntPtr FPDFPage_GetObject(IntPtr page,int index);
  [DllImport(Dll)] static extern int FPDFPageObj_GetType(IntPtr obj);
  [DllImport(Dll)] static extern int FPDFPageObj_GetBounds(IntPtr obj,out float left,out float bottom,out float right,out float top);
  static readonly object Gate=new object();
  static bool initialized;
  static void WithDocument(string source,Action<IntPtr> action) {
   lock(Gate) { if(!initialized) { FPDF_InitLibrary(); initialized=true; }
    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
     if(input.Length>200*1024*1024) throw new InvalidDataException("PDF exceeds the 200 MB limit.");
     byte[] bytes=new byte[(int)input.Length]; int offset=0; while(offset<bytes.Length) { int read=input.Read(bytes,offset,bytes.Length-offset); if(read==0) throw new EndOfStreamException(); offset+=read; }
     var pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
     try { IntPtr doc=FPDF_LoadMemDocument64(bytes,(UIntPtr)bytes.Length,IntPtr.Zero); if(doc==IntPtr.Zero) throw new InvalidDataException("This PDF is damaged or password protected."); try { action(doc); } finally { FPDF_CloseDocument(doc); } } finally { pinned.Free(); }
    }
   }
  }
  public static void Validate(string path) { WithDocument(path,delegate(IntPtr doc){if(FPDF_GetPageCount(doc)<1) throw new InvalidDataException("PDF has no pages.");}); }
  internal sealed class TextPage {public string Text;public double Width,Height;public bool LargeImage;}
  static bool HasLargeImage(IntPtr page){double area=FPDF_GetPageWidth(page)*FPDF_GetPageHeight(page);int count=FPDFPage_CountObjects(page);for(int i=0;i<Math.Min(count,10000);i++){IntPtr obj=FPDFPage_GetObject(page,i);float l,b,r,t;if(FPDFPageObj_GetType(obj)==3&&FPDFPageObj_GetBounds(obj,out l,out b,out r,out t)!=0&&Math.Abs((r-l)*(t-b))>area*0.4)return true;}return false;}
  internal static List<TextPage> ReadPages(string source) {
   var pages=new List<TextPage>();
   WithDocument(source,delegate(IntPtr doc){int count=FPDF_GetPageCount(doc);if(count<1||count>200)throw new InvalidDataException("PDF conversion supports 1–200 pages.");
    for(int i=0;i<count;i++){IntPtr page=FPDF_LoadPage(doc,i);if(page==IntPtr.Zero)throw new InvalidDataException("Could not read PDF page.");
     try{IntPtr text=FPDFText_LoadPage(page);if(text==IntPtr.Zero)throw new InvalidDataException("Could not read PDF text.");
      try{int n=FPDFText_CountChars(text);if(n<0||n>5000000)throw new InvalidDataException("PDF page text exceeds the limit.");var buffer=new ushort[n+1];int read=FPDFText_GetText(text,0,n,buffer);var chars=new char[Math.Max(0,read-1)];for(int j=0;j<chars.Length;j++)chars[j]=(char)buffer[j];pages.Add(new TextPage{Text=new string(chars),Width=FPDF_GetPageWidth(page),Height=FPDF_GetPageHeight(page),LargeImage=HasLargeImage(page)});}finally{FPDFText_ClosePage(text);}
     }finally{FPDF_ClosePage(page);}
    }
   });return pages;
  }
  internal static void RenderForOcr(string source,int index,string output) {
   WithDocument(source,delegate(IntPtr doc){
    IntPtr page=FPDF_LoadPage(doc,index);if(page==IntPtr.Zero)throw new InvalidDataException("Could not render PDF page.");
    try{double pw=FPDF_GetPageWidth(page),ph=FPDF_GetPageHeight(page);if(pw<=0||ph<=0||pw>20000||ph>20000)throw new InvalidDataException("Invalid PDF page dimensions.");
     double scale=Math.Min(300.0/72.0,Math.Min(10000.0/Math.Max(pw,ph),Math.Sqrt(25000000.0/(pw*ph))));int w=(int)Math.Ceiling(pw*scale),h=(int)Math.Ceiling(ph*scale);
     IntPtr bitmap=FPDFBitmap_Create(w,h,0);if(bitmap==IntPtr.Zero)throw new OutOfMemoryException("Could not allocate OCR page.");
     try{FPDFBitmap_FillRect(bitmap,0,0,w,h,0xffffffff);FPDF_RenderPageBitmap(bitmap,page,0,0,w,h,0,1);using(var image=new Bitmap(w,h,FPDFBitmap_GetStride(bitmap),PixelFormat.Format32bppRgb,FPDFBitmap_GetBuffer(bitmap))){image.SetResolution(300,300);image.Save(output,ImageFormat.Png);}}
     finally{FPDFBitmap_Destroy(bitmap);}
    }finally{FPDF_ClosePage(page);}
   });
  }
  public static string Export(string source,string target) {
   string work=Path.Combine(Path.GetDirectoryName(source),".dropshift-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
   try {
    WithDocument(source,delegate(IntPtr doc) {
     int count=FPDF_GetPageCount(doc); if(count<1 || count>200) throw new InvalidDataException("PDF export supports 1–200 pages per file.");
     var text=new StringBuilder(); bool anyText=false;
     for(int i=0;i<count;i++) {
      IntPtr page=FPDF_LoadPage(doc,i); if(page==IntPtr.Zero) throw new InvalidDataException("Could not read page "+(i+1));
      try {
       if(target=="TXT") {
        IntPtr tp=FPDFText_LoadPage(page); if(tp==IntPtr.Zero) throw new InvalidDataException("Could not extract page text.");
        try { int n=FPDFText_CountChars(tp); if(n<0 || n>5000000) throw new InvalidDataException("Page text exceeds the limit."); var buffer=new ushort[n+1]; int read=FPDFText_GetText(tp,0,n,buffer); var chars=new char[Math.Max(0,read-1)]; for(int j=0;j<chars.Length;j++) chars[j]=(char)buffer[j]; string content=new string(chars); anyText|=!String.IsNullOrWhiteSpace(content); text.AppendLine("--- Page "+(i+1)+" ---").AppendLine(content).AppendLine(); } finally { FPDFText_ClosePage(tp); }
       } else {
        double pw=FPDF_GetPageWidth(page),ph=FPDF_GetPageHeight(page); if(pw<=0 || ph<=0 || pw>20000 || ph>20000) throw new InvalidDataException("Invalid PDF page dimensions.");
        int w=(int)Math.Ceiling(pw*2),h=(int)Math.Ceiling(ph*2); if((long)w*h>50000000) throw new InvalidDataException("PDF page exceeds the 50 megapixel limit at 144 DPI.");
        IntPtr bm=FPDFBitmap_Create(w,h,0); if(bm==IntPtr.Zero) throw new OutOfMemoryException("Could not allocate page bitmap.");
        try { FPDFBitmap_FillRect(bm,0,0,w,h,0xffffffff); FPDF_RenderPageBitmap(bm,page,0,0,w,h,0,1);
         using(var image=new Bitmap(w,h,FPDFBitmap_GetStride(bm),PixelFormat.Format32bppRgb,FPDFBitmap_GetBuffer(bm))) {
          string path=Path.Combine(work,"Page "+(i+1).ToString("D3")+"."+target.ToLowerInvariant());
          if(target=="PNG") image.Save(path,ImageFormat.Png); else using(var ep=new EncoderParameters(1)) { ep.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,92L); image.Save(path,Array.Find(ImageCodecInfo.GetImageEncoders(),x=>x.FormatID==ImageFormat.Jpeg.Guid),ep); }
          using(var check=Image.FromFile(path)) if(check.Width!=w || check.Height!=h) throw new InvalidDataException("Page output failed validation.");
         }
        } finally { FPDFBitmap_Destroy(bm); }
       }
      } finally { FPDF_ClosePage(page); }
     }
     if(target=="TXT") { if(!anyText) throw new InvalidDataException("This PDF contains no extractable text. Scanned pages need OCR; export images instead."); File.WriteAllText(Path.Combine(work,"text.txt"),text.ToString(),new UTF8Encoding(false)); }
    });
    return target=="TXT" ? Formats.Publish(Path.Combine(work,"text.txt"),source,".txt",false) : Formats.Publish(work,source," "+target+" pages",true);
   } finally { if(Directory.Exists(work)) Directory.Delete(work,true); }
  }
  // Small image-only PDF writer: keeps every pixel in the JPEG stream, with explicit xref offsets.
  public static void ImageToPdf(string jpg,int width,int height,string output) { WriteImages(new[]{File.ReadAllBytes(jpg)},new[]{new Size(width,height)},output); }
  static void WriteImages(byte[][] images,Size[] sizes,string output) {
   using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
    Action<string> put=s=>{byte[] b=Encoding.ASCII.GetBytes(s);stream.Write(b,0,b.Length);}; var offsets=new List<long>{0};
    Action<int,string> obj=(id,body)=>{offsets.Add(stream.Position);put(id+" 0 obj\n"+body+"\nendobj\n");};
    put("%PDF-1.4\n");obj(1,"<< /Type /Catalog /Pages 2 0 R >>"); var kids=new StringBuilder(); for(int i=0;i<images.Length;i++) kids.Append((3+i*3)+" 0 R ");obj(2,"<< /Type /Pages /Count "+images.Length+" /Kids ["+kids+"] >>");
    for(int i=0;i<images.Length;i++) { int id=3+i*3,w=sizes[i].Width,h=sizes[i].Height;
     obj(id,"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "+w+" "+h+"] /Resources << /XObject << /Im "+(id+1)+" 0 R >> >> /Contents "+(id+2)+" 0 R >>");
     offsets.Add(stream.Position); put((id+1)+" 0 obj\n<< /Type /XObject /Subtype /Image /Width "+w+" /Height "+h+" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length "+images[i].Length+" >>\nstream\n");stream.Write(images[i],0,images[i].Length);put("\nendstream\nendobj\n");
     string commands="q "+w+" 0 0 "+h+" 0 0 cm /Im Do Q\n";obj(id+2,"<< /Length "+commands.Length+" >>\nstream\n"+commands+"endstream");
    }
    long xref=stream.Position;put("xref\n0 "+offsets.Count+"\n0000000000 65535 f \n"); for(int i=1;i<offsets.Count;i++) put(offsets[i].ToString("D10")+" 00000 n \n"); put("trailer\n<< /Size "+offsets.Count+" /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");stream.Flush(true);
   }
  }
  public static void TextToPdf(Stream source,string output) {
   if(source.Length>1024*1024) throw new InvalidDataException("Text export supports files up to 1 MB."); string text; using(var reader=new StreamReader(source,new UTF8Encoding(false,true),true,4096,true)) text=reader.ReadToEnd();
   if(text.IndexOf('\0')>=0) throw new InvalidDataException("This is not a plain text file.");
   var pages=new List<byte[]>();var sizes=new List<Size>();
   using(var font=new Font("Segoe UI",16)) using(var sf=new StringFormat(StringFormat.GenericTypographic)) {
    sf.FormatFlags=StringFormatFlags.LineLimit;
    do { using(var bmp=new Bitmap(1240,1754,PixelFormat.Format24bppRgb)) using(var g=Graphics.FromImage(bmp)) {
      g.Clear(Color.White); int chars,lines; g.MeasureString(text,font,new SizeF(1080,1570),sf,out chars,out lines); if(chars==0 && text.Length>0) throw new InvalidDataException("Could not lay out text.");
      g.DrawString(text.Substring(0,chars),font,Brushes.Black,new RectangleF(80,80,1080,1570),sf); using(var ms=new MemoryStream()){bmp.Save(ms,ImageFormat.Jpeg);pages.Add(ms.ToArray());sizes.Add(bmp.Size);} text=text.Substring(chars);
     } if(pages.Count>200) throw new InvalidDataException("Text exceeds 200 pages.");
    }while(text.Length>0);
   }
   WriteImages(pages.ToArray(),sizes.ToArray(),output);
  }
 }
}
