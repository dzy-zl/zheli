using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Zheli.Platform;

public sealed record DocumentFragment(string Location,string Text);
public sealed record ExtractedDocument(List<DocumentFragment> Fragments,string? Notice=null);

// Read-only extraction. No Office automation, macro execution, external relationships or formula evaluation.
public sealed class DocumentTextExtractor
{
    public const int Version=1,MaximumFileBytes=32*1024*1024,MaximumCharacters=2_000_000;
    public static readonly HashSet<string> Extensions=new(StringComparer.OrdinalIgnoreCase){".txt",".md",".pdf",".docx",".xlsx",".pptx"};
    public ExtractedDocument Extract(byte[] bytes,string extension,CancellationToken ct)
    {
        if(bytes.Length>MaximumFileBytes)throw new InvalidDataException("文件超过32MB限制。");
        var fragments=new List<DocumentFragment>();var characters=0;
        void Add(string location,string text)
        {
            ct.ThrowIfCancellationRequested();text=text.Trim();if(text.Length==0)return;
            characters+=text.Length;if(characters>MaximumCharacters)throw new InvalidDataException("文档文字超过200万字符限制。");
            // Overlap avoids losing a literal keyword across chunk boundaries; queries are limited to 80 characters.
            for(var start=0;start<text.Length;start+=920)
            {
                if(fragments.Count>=10000)throw new InvalidDataException("文档片段过多。");
                var take=Math.Min(1000,text.Length-start);fragments.Add(new(location,text.Substring(start,take)));
                if(start+take==text.Length)break;
            }
        }
        extension=extension.ToLowerInvariant();
        if(extension is ".txt" or ".md")
        {
            using var reader=new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),true);
            var line=0;while(reader.ReadLine() is { } text)Add($"第{++line}行",text);
            return new(fragments);
        }
        if(extension==".pdf")
        {
            using var pdf=PdfDocument.Open(bytes);
            if(pdf.NumberOfPages>500)throw new InvalidDataException("PDF超过500页限制。");
            var empty=0;
            for(var page=1;page<=pdf.NumberOfPages;page++)
            {ct.ThrowIfCancellationRequested();var text=ContentOrderTextExtractor.GetText(pdf.GetPage(page));if(string.IsNullOrWhiteSpace(text))empty++;Add($"第{page}页",text);}
            return new(fragments,empty>0?$"{empty}页没有可提取文本，可能是扫描页；尚未进行OCR。":null);
        }
        if(!Extensions.Contains(extension))throw new InvalidDataException("此文件格式尚不支持。");
        using var archive=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
        if(archive.Entries.Count>4096||archive.Entries.Sum(e=>e.Length)>128L*1024*1024)throw new InvalidDataException("文档解压大小或条目数超限。");
        if(archive.Entries.Select(e=>e.FullName).Distinct(StringComparer.Ordinal).Count()!=archive.Entries.Count)throw new InvalidDataException("文档包含重复部件。");
        XDocument Read(string part)
        {
            ct.ThrowIfCancellationRequested();var entry=archive.GetEntry(part)??throw new InvalidDataException("缺少文档部件："+part);
            if(entry.Length>16*1024*1024)throw new InvalidDataException("XML部件过大。");
            using var stream=entry.Open();using var xml=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=16*1024*1024});
            return XDocument.Load(xml,LoadOptions.None);
        }
        Dictionary<string,string> Relationships(string part)
        {
            var directory=part[..(part.LastIndexOf('/')+1)];var relPath=directory+"_rels/"+part[(part.LastIndexOf('/')+1)..]+".rels";
            var result=new Dictionary<string,string>();
            foreach(var rel in Read(relPath).Root!.Elements())
            {
                if((string?)rel.Attribute("TargetMode")=="External")continue;
                var id=(string?)rel.Attribute("Id");var target=(string?)rel.Attribute("Target");if(id==null||target==null)continue;
                if(target.Contains(':')||target.Contains('\\')||target.Contains('?')||target.Contains('#'))throw new InvalidDataException("文档部件路径无效。");
                var normalized=new List<string>();
                foreach(var segment in (target.StartsWith('/')?target[1..]:directory+target).Split('/'))
                {if(segment is "" or ".")continue;if(segment==".."){if(normalized.Count==0)throw new InvalidDataException("部件路径越界。");normalized.RemoveAt(normalized.Count-1);}else normalized.Add(segment);}
                result.Add(id,string.Join('/',normalized));
            }
            return result;
        }
        static string Text(XElement element,string name="t")=>string.Concat(element.Descendants().Where(n=>n.Name.LocalName==name).Select(n=>n.Value));
        static string RelationId(XElement node)=>node.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id"&&a.Name.NamespaceName.Length>0)?.Value??throw new InvalidDataException("缺少文档关系编号。");
        if(extension==".docx")
        {
            var document=Read("word/document.xml");var w=document.Root!.Name.Namespace;var paragraph=0;
            foreach(var p in document.Descendants(w+"body").Descendants(w+"p"))
            {
                paragraph++;var text=string.Concat(p.Descendants().Where(n=>n.Name==w+"t"&&!n.Ancestors(w+"del").Any()).Select(n=>n.Value));
                Add($"正文第{paragraph}段",text);
            }
            return new(fragments,"Word定位使用正文段落号，不虚构受排版影响的页码；不含页眉页脚和批注。");
        }
        if(extension==".pptx")
        {
            var presentation=Read("ppt/presentation.xml");var p=presentation.Root!.Name.Namespace;var rels=Relationships("ppt/presentation.xml");var slide=0;
            foreach(var id in presentation.Descendants(p+"sldId"))
            {
                ct.ThrowIfCancellationRequested();slide++;if(slide>500)throw new InvalidDataException("幻灯片超过500页。");
                if(!rels.TryGetValue(RelationId(id),out var part))throw new InvalidDataException("无法读取幻灯片关系。");
                var doc=Read(part);Add($"第{slide}张幻灯片",string.Join('\n',doc.Descendants().Where(n=>n.Name.LocalName=="p").Select(n=>Text(n))));
            }
            return new(fragments,"仅提取幻灯片正文；图片和演讲者备注未识别。");
        }
        var book=Read("xl/workbook.xml");var ns=book.Root!.Name.Namespace;var mappings=Relationships("xl/workbook.xml");
        var shared=archive.GetEntry("xl/sharedStrings.xml")==null?[]:Read("xl/sharedStrings.xml").Root!.Elements().Select(n=>Text(n)).ToList();
        foreach(var sheet in book.Descendants(ns+"sheet"))
        {
            ct.ThrowIfCancellationRequested();if(!mappings.TryGetValue(RelationId(sheet),out var part))throw new InvalidDataException("无法读取工作表关系。");
            var sheetName=(string?)sheet.Attribute("name")??"未命名工作表";var doc=Read(part);var s=doc.Root!.Name.Namespace;
            var visibility=(string?)sheet.Attribute("state");
            foreach(var cell in doc.Descendants(s+"c"))
            {
                var address=(string?)cell.Attribute("r")??"未标注单元格";var kind=(string?)cell.Attribute("t");var value=cell.Element(s+"v")?.Value??"";
                if(kind=="s")value=int.TryParse(value,out var index)&&index>=0&&index<shared.Count?shared[index]:throw new InvalidDataException("共享字符串编号无效。");
                else if(kind=="inlineStr")value=Text(cell);
                if(cell.Element(s+"f")!=null)value=string.IsNullOrEmpty(value)?"[公式没有已保存的计算结果]":"[公式缓存值] "+value;
                Add($"工作表「{sheetName}」 · {address}"+(visibility is "hidden" or "veryHidden"?" · 隐藏工作表":""),value);
            }
        }
        return new(fragments,"Excel展示保存的单元格原值/公式缓存值，不重新计算公式或模拟显示格式；隐藏工作表会明确标记。");
    }
}
