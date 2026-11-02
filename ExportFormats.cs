using System;using System.Collections.Generic;using System.Web.Script.Serialization;
namespace DropShift {static class ExportFormats {
 static void Main(){var result=new SortedDictionary<string,string[]>();
 foreach(string ext in "jpg jpeg png webp bmp tif tiff gif pdf txt doc docx docm odt rtf xls xlsx xlsm csv tsv ods ppt pptx pptm odp mp3 wav flac ogg m4a aac wma opus aiff mp4 mov mkv avi webm m4v wmv mpeg mpg".Split(' '))result[ext]=Formats.ForFile("sample."+ext);
 Console.WriteLine(new JavaScriptSerializer().Serialize(result));
 }}}
