using System;using System.IO;using System.Linq;using System.Text;using System.Diagnostics;using System.Collections.Generic;using System.Web.Script.Serialization;using System.Xml;using System.Xml.Linq;using System.IO.Compression;using System.Threading.Tasks;
namespace DropShift {
 static class OcrEngine {
  internal static int Letters(string text){return text.Count(Char.IsLetterOrDigit);}
  static bool NeedsOcr(PdfEngine.TextPage page){int count=Letters(page.Text);return count<20||(page.LargeImage&&count<200);}
  public static string ConvertPdf(string source){
   using(var original=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)){
    var pages=PdfEngine.ReadPages(source);
    if(pages.All(p=>!NeedsOcr(p)))return OfficeEngine.Convert(source,"DOCX");
    string work=Path.Combine(Path.GetDirectoryName(source),".dropshift-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
    try{
     var texts=new List<string>();int recognized=0,low=0;var duration=Stopwatch.StartNew();
     for(int i=0;i<pages.Count;i++){
      if(duration.Elapsed.TotalMinutes>10)throw new IOException("OCR exceeded the 10-minute limit. Original kept.");
      if(!NeedsOcr(pages[i])){texts.Add(pages[i].Text);continue;}
      Formats.Report("Reading scanned page "+(i+1)+" of "+pages.Count+" (English OCR)…");
      string png=Path.Combine(work,"page.png"),json=Path.Combine(work,"page.json");
      PdfEngine.RenderForOcr(source,i,png);var result=Recognize(png,json);
      string text=Convert.ToString(result["text"]);double confidence=Convert.ToDouble(result["confidence"]);
      // A native text snippet is preferable to an empty OCR result on a sparse page.
      if(Letters(text)==0)text=pages[i].Text;
      if(Letters(text)==0&&pages[i].LargeImage)throw new InvalidDataException("No readable text was found on scanned page "+(i+1)+". Use a clearer English scan.");
      if(Letters(text)>0){recognized++;if(confidence<0.60)low++;}
      texts.Add(text);File.Delete(png);File.Delete(json);
     }
     if(texts.All(t=>Letters(t)==0))throw new InvalidDataException("No readable text was found. Use a clearer scan; the original is unchanged.");
     string output=Path.Combine(work,"result.docx");WriteDocx(texts,output);
     using(var zip=ZipFile.OpenRead(output)){if(zip.GetEntry("word/document.xml")==null)throw new InvalidDataException("OCR Word output failed validation.");}
     Formats.Notice="Editable Word created using English OCR ("+recognized+" page(s)). Review recognized text"+(low>0?"; "+low+" page(s) had low confidence":"")+". Layout is reflowed; complex tables and images are not preserved.";
     Log.Write("OCR completed: "+recognized+" recognized page(s), "+low+" low-confidence page(s)");
     return Formats.Publish(output,source,".docx",false);
    }finally{if(Directory.Exists(work))Directory.Delete(work,true);}
   }
  }
  static Dictionary<string,object> Recognize(string png,string json){
   string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DropShift.Ocr.exe");if(!File.Exists(exe))throw new IOException("OCR engine is missing. Extract the complete app package.");
   var info=new ProcessStartInfo(exe,"\""+png+"\" \""+json+"\" "+Process.GetCurrentProcess().Id){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
   using(var process=Process.Start(info)){
    var error=process.StandardError.ReadToEndAsync();var stdout=process.StandardOutput.ReadToEndAsync();
    if(!process.WaitForExit(120000)){process.Kill();process.WaitForExit();throw new IOException("OCR timed out on a page. Original kept.");}
    if(process.ExitCode!=0||!File.Exists(json))throw new IOException("OCR could not read this page. "+error.Result.Trim());
   }
   if(new FileInfo(json).Length>10000000)throw new InvalidDataException("OCR text exceeds the page limit.");
   return new JavaScriptSerializer{MaxJsonLength=10000000}.Deserialize<Dictionary<string,object>>(File.ReadAllText(json));
  }
  static void WriteDocx(List<string> pages,string output){
   XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
   var body=new XElement(w+"body");
   for(int i=0;i<pages.Count;i++){
    if(i>0)body.Add(new XElement(w+"p",new XElement(w+"r",new XElement(w+"br",new XAttribute(w+"type","page")))));
    string clean=new string(pages[i].Where(c=>XmlConvert.IsXmlChar(c)).ToArray()).Replace("\r","");
    foreach(string paragraph in clean.Split(new[]{"\n\n"},StringSplitOptions.RemoveEmptyEntries)){
     string text=paragraph.Trim();if(text.Length==0)continue;
     var run=new XElement(w+"r",new XElement(w+"rPr",new XElement(w+"rFonts",new XAttribute(w+"ascii","Arial"),new XAttribute(w+"hAnsi","Arial")),new XElement(w+"sz",new XAttribute(w+"val","22"))));
     string[] rows=text.Split('\n');for(int j=0;j<rows.Length;j++){if(j>0)run.Add(new XElement(w+"br"));run.Add(new XElement(w+"t",new XAttribute(XNamespace.Xml+"space","preserve"),rows[j]));}
     body.Add(new XElement(w+"p",new XElement(w+"pPr",new XElement(w+"spacing",new XAttribute(w+"after","120"))),run));
    }
   }
   body.Add(new XElement(w+"sectPr",new XElement(w+"pgSz",new XAttribute(w+"w","11906"),new XAttribute(w+"h","16838")),new XElement(w+"pgMar",new XAttribute(w+"top","1008"),new XAttribute(w+"bottom","1008"),new XAttribute(w+"left","1008"),new XAttribute(w+"right","1008"))));
   using(var zip=ZipFile.Open(output,ZipArchiveMode.Create)){
    Put(zip,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
    Put(zip,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
    Put(zip,"word/document.xml",new XDocument(new XElement(w+"document",new XAttribute(XNamespace.Xmlns+"w",w),body)).ToString());
   }
  }
  static void Put(ZipArchive zip,string name,string text){using(var stream=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)))stream.Write(text);}
 }
}
