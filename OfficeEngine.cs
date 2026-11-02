using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Threading.Tasks;using System.IO.Compression;using System.Xml;using System.Xml.Linq;using System.Text;using System.Threading;
namespace DropShift {
 static class OfficeEngine {
  static readonly string[] Word={"DOC","DOCX","DOCM","ODT","RTF"},Sheet={"XLS","XLSX","XLSM","CSV","TSV","ODS"},Slides={"PPT","PPTX","PPTM","ODP"};
  public static string Format(string p){string f=Path.GetExtension(p).TrimStart('.').ToUpperInvariant();return Word.Contains(f)||Sheet.Contains(f)||Slides.Contains(f)?f:null;}
  public static string[] Targets(string p){string f=Format(p);if(f==null)return new string[0];return (Word.Contains(f)?new[]{"PDF","DOCX","ODT","TXT"}:Sheet.Contains(f)?new[]{"PDF","XLSX","ODS"}:new[]{"PDF","PPTX","ODP"}).Where(x=>x!=f).ToArray();}
  static string Q(string s){return "\""+s+"\"";}
  static void ValidatePackage(string source){
   string format=Format(source),part=null;
   if(format=="DOCX"||format=="DOCM")part="word/document.xml";
   else if(format=="XLSX"||format=="XLSM")part="xl/workbook.xml";
   else if(format=="PPTX"||format=="PPTM")part="ppt/presentation.xml";
   else if(format=="ODT"||format=="ODS"||format=="ODP")part="content.xml";
   if(part!=null)using(var zip=ZipFile.OpenRead(source)){var entry=zip.GetEntry(part);if(entry==null||entry.Length>50000000)throw new InvalidDataException("Office document is damaged, encrypted or too large.");}
  }
  public static string Convert(string source,string target){
   return ConvertLocked(source,target);
  }
  static void ExtractText(string source,string output){
   bool docx=Path.GetExtension(source).Equals(".docx",StringComparison.OrdinalIgnoreCase);
   using(var zip=ZipFile.OpenRead(source)){
    var entry=zip.GetEntry(docx?"word/document.xml":"content.xml");
    if(entry==null||entry.Length>50000000)throw new InvalidDataException("Document text is missing or too large.");
    using(var stream=entry.Open())using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=50000000})){
     var xml=XDocument.Load(reader);XNamespace ns=docx?"http://schemas.openxmlformats.org/wordprocessingml/2006/main":"urn:oasis:names:tc:opendocument:xmlns:text:1.0";
     var text=new StringBuilder();
     foreach(var p in xml.Descendants().Where(e=>e.Name==ns+"p"||(!docx&&e.Name==ns+"h"))){
      if(docx){foreach(var e in p.Descendants()){if(e.Name==ns+"t")text.Append(e.Value);else if(e.Name==ns+"tab")text.Append('\t');else if(e.Name==ns+"br"||e.Name==ns+"cr")text.AppendLine();}}
      else text.Append(p.Value);
      text.AppendLine();
     }
     File.WriteAllText(output,text.ToString(),new UTF8Encoding(false));
    }
   }
  }
  static string ConvertLocked(string source,string target){
   string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Office","program","soffice.com");if(!File.Exists(exe))throw new IOException("Office engine is missing. Extract the full app package.");
   string work=Path.Combine(Path.GetDirectoryName(source),".dropshift-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
   try{using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(input.Length>200*1024*1024)throw new InvalidDataException("Office input exceeds the 200 MB limit.");
    ValidatePackage(source);
    string copy=Path.Combine(work,Path.GetFileName(source));using(var output=File.Create(copy))input.CopyTo(output);
    if(target=="TXT"&&(Format(source)=="DOCX"||Format(source)=="ODT")){string text=Path.Combine(work,"result.txt");ExtractText(copy,text);return Formats.Publish(text,source,".txt",false);}
    string filter=target=="TXT"?"DOCX":target;
    string result=Path.Combine(work,"result."+filter.ToLowerInvariant());
    if(Path.GetExtension(source).Equals(".pdf",StringComparison.OrdinalIgnoreCase))PdfEngine.Validate(copy);
    OfficeWorker.Convert(copy,filter,result);
    if(!File.Exists(result)||new FileInfo(result).Length==0)throw new InvalidDataException("Office output is empty. Original kept.");
    if(target=="TXT"){string text=Path.Combine(work,"result.txt");ExtractText(result,text);result=text;}
    if(target=="PDF")PdfEngine.Validate(result);else if(target!="TXT")using(var zip=ZipFile.OpenRead(result)){if(zip.Entries.Count==0)throw new InvalidDataException("Output document is empty.");}
    return Formats.Publish(result,source,"."+target.ToLowerInvariant(),false);
   }}finally{try{if(Directory.Exists(work))Directory.Delete(work,true);}catch(IOException){Log.Write("Office temporary files remain: "+work);}}
  }
 }
}
