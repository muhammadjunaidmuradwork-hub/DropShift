using System;
using System.IO;
using System.Linq;
using System.Drawing;
namespace DropShift {
 static class Formats {
  [ThreadStatic] public static string Notice;
  public static Action<string> Progress;
  public static void Report(string text){var handler=Progress;if(handler!=null)handler(text);}
  public static string Format(string path) { string e=Path.GetExtension(path).ToLowerInvariant(); return e==".pdf"?"PDF": e==".txt"?"TXT":ImageConverter.Format(path) ?? MediaEngine.Format(path) ?? OfficeEngine.Format(path); }
  public static string[] ForFile(string file) { string f=Format(file); if(MediaEngine.Format(file)!=null) return MediaEngine.Targets(file); if(OfficeEngine.Format(file)!=null) return OfficeEngine.Targets(file); if(f=="PDF") return new[]{"JPG","PNG","TXT","DOCX"}; if(f=="TXT") return new[]{"PDF"}; if(f==null) return new string[0]; return new[]{"JPG","PNG","WebP","BMP","TIFF","PDF"}.Where(x=>x!=f).ToArray(); }
  public static string[] Targets(string[] files) { if(files==null || files.Length==0 || files.Length>100) return new string[0]; return ForFile(files[0]).Where(t=>files.All(f=>ForFile(f).Contains(t))).ToArray(); }
  public static string Publish(string temp,string source,string extension,bool directory) {
   string folder=Path.GetDirectoryName(source),stem=Path.GetFileNameWithoutExtension(source)+" (converted)";
   for(int n=0;n<10000;n++) { string dest=Path.Combine(folder,stem+(n==0?"":" "+(n+1))+extension); try { if(directory) Directory.Move(temp,dest); else File.Move(temp,dest); return dest; } catch(IOException) { if(!File.Exists(dest) && !Directory.Exists(dest)) throw; } }
   throw new IOException("Could not find an unused output name.");
  }
  public static string Convert(string source,string target) {
   Notice=null;
   source=Path.GetFullPath(source); if(!Targets(new[]{source}).Contains(target)) throw new InvalidDataException("This file has no supported conversion to "+target+".");
   if(MediaEngine.Format(source)!=null) return MediaEngine.Convert(source,target);
   if(OfficeEngine.Format(source)!=null) return OfficeEngine.Convert(source,target);
   if(Format(source)=="PDF") return target=="DOCX" ? OcrEngine.ConvertPdf(source) : PdfEngine.Export(source,target);
   if(target!="PDF") return ImageConverter.Convert(source,target);
   string work=Path.Combine(Path.GetDirectoryName(source),".dropshift-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
   try {
    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)) {
     string output=Path.Combine(work,"result.pdf");
     if(Format(source)=="TXT") PdfEngine.TextToPdf(input,output);
     else { string copy=Path.Combine(work,"input"+Path.GetExtension(source)); using(var o=File.Create(copy)) input.CopyTo(o); string jpg=ImageConverter.Format(copy)=="JPG" ? ImageConverter.Convert(copy,"PNG") : copy; jpg=ImageConverter.Convert(jpg,"JPG"); using(var image=new Bitmap(jpg)) PdfEngine.ImageToPdf(jpg,image.Width,image.Height,output); }
     PdfEngine.Validate(output); return Publish(output,source,".pdf",false);
    }
   } finally { if(Directory.Exists(work)) Directory.Delete(work,true); }
  }
 }
}
