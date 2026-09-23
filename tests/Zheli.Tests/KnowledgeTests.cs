using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using Zheli.Domain;
using Zheli.Platform;

internal static class KnowledgeTests
{
    public static void Run(Action<string,Action> test,string directory)
    {
        var extractor=new DocumentTextExtractor();var ct=CancellationToken.None;
        static void Equal<T>(T expected,T actual){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new Exception($"expected {expected}, got {actual}");}
        static void Deny(Action action)
        {try{action();}catch(Exception e)when(e is InvalidDataException or UnauthorizedAccessException or InvalidOperationException or DomainException or ArgumentException or System.Xml.XmlException){return;}throw new Exception("expected rejection");}
        static byte[] Zip(params (string path,string xml)[] entries)
        {
            using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true))
                foreach(var (path,xml) in entries){using var writer=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false));writer.Write(xml);}
            return bytes.ToArray();
        }
        const string w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string p="http://schemas.openxmlformats.org/presentationml/2006/main";
        const string a="http://schemas.openxmlformats.org/drawingml/2006/main";
        const string r="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string relationships="http://schemas.openxmlformats.org/package/2006/relationships";
        test("document TXT preserves line number",()=>{var d=extractor.Extract(Encoding.UTF8.GetBytes("第一行\n高等数学复习"),".txt",ct);Equal("第2行",d.Fragments[1].Location);});
        test("document UTF16 BOM is detected",()=>{var b=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("会计复习")).ToArray();Equal("会计复习",extractor.Extract(b,".txt",ct).Fragments[0].Text);});
        test("long text retains cross-chunk keyword",()=>{var d=extractor.Extract(Encoding.UTF8.GetBytes(new string('a',995)+"浙江大学复习安排"+new string('b',100)),".md",ct);Equal(true,d.Fragments.Any(f=>f.Text.Contains("浙江大学复习安排")));});
        test("DOCX joins runs and reports paragraphs",()=>
        {
            var bytes=Zip(("word/document.xml",$"<w:document xmlns:w='{w}'><w:body><w:p><w:r><w:t>高等</w:t></w:r><w:r><w:t>数学</w:t></w:r><w:del><w:r><w:t>废弃内容</w:t></w:r></w:del></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>第二段</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>"));
            var doc=extractor.Extract(bytes,".docx",ct);Equal("高等数学",doc.Fragments[0].Text);Equal("正文第2段",doc.Fragments[1].Location);
        });
        test("PPTX follows presentation relationship order",()=>
        {
            var bytes=Zip(("ppt/presentation.xml",$"<p:presentation xmlns:p='{p}' xmlns:r='{r}'><p:sldIdLst><p:sldId id='256' r:id='later'/><p:sldId id='257' r:id='first'/></p:sldIdLst></p:presentation>"),
                ("ppt/_rels/presentation.xml.rels",$"<Relationships xmlns='{relationships}'><Relationship Id='first' Target='slides/slide1.xml'/><Relationship Id='later' Target='slides/slide3.xml'/></Relationships>"),
                ("ppt/slides/slide1.xml",$"<p:sld xmlns:p='{p}' xmlns:a='{a}'><a:p><a:r><a:t>后显示</a:t></a:r></a:p></p:sld>"),
                ("ppt/slides/slide3.xml",$"<p:sld xmlns:p='{p}' xmlns:a='{a}'><a:p><a:r><a:t>先显示</a:t></a:r></a:p></p:sld>"));
            var doc=extractor.Extract(bytes,".pptx",ct);Equal("先显示",doc.Fragments[0].Text);Equal("第2张幻灯片",doc.Fragments[1].Location);
        });
        test("XLSX preserves sheet cell cached formula and hidden status",()=>
        {
            var bytes=Zip(("xl/workbook.xml",$"<workbook xmlns='{s}' xmlns:r='{r}'><sheets><sheet name='预算' state='hidden' r:id='budget'/></sheets></workbook>"),
                ("xl/_rels/workbook.xml.rels",$"<Relationships xmlns='{relationships}'><Relationship Id='budget' Target='worksheets/data.xml'/></Relationships>"),
                ("xl/sharedStrings.xml",$"<sst xmlns='{s}'><si><r><t>校园</t></r><r><t>开支</t></r></si></sst>"),
                ("xl/worksheets/data.xml",$"<worksheet xmlns='{s}'><sheetData><row><c r='B2' t='s'><v>0</v></c><c r='C2' t='inlineStr'><is><t>食堂</t></is></c><c r='D2'><f>SUM(A1:A2)</f><v>300</v></c></row></sheetData></worksheet>"));
            var doc=extractor.Extract(bytes,".xlsx",ct);Equal("校园开支",doc.Fragments[0].Text);Equal(true,doc.Fragments[0].Location.Contains("预算")&&doc.Fragments[0].Location.Contains("B2")&&doc.Fragments[0].Location.Contains("隐藏"));Equal("食堂",doc.Fragments[1].Text);Equal("[公式缓存值] 300",doc.Fragments[2].Text);
        });
        test("OOXML prohibits DTD entity expansion",()=>Deny(()=>extractor.Extract(Zip(("word/document.xml",$"<!DOCTYPE document [<!ENTITY secret SYSTEM 'file:///etc/passwd'>]><w:document xmlns:w='{w}'><w:body><w:p><w:r><w:t>&secret;</w:t></w:r></w:p></w:body></w:document>")),".docx",ct)));
        test("OOXML does not follow external slide relationships",()=>Deny(()=>extractor.Extract(Zip(("ppt/presentation.xml",$"<p:presentation xmlns:p='{p}' xmlns:r='{r}'><p:sldIdLst><p:sldId r:id='ext'/></p:sldIdLst></p:presentation>"),("ppt/_rels/presentation.xml.rels",$"<Relationships xmlns='{relationships}'><Relationship Id='ext' Target='https://example.invalid/slide.xml' TargetMode='External'/></Relationships>")),".pptx",ct)));
        test("OOXML rejects oversized XML part",()=>Deny(()=>extractor.Extract(Zip(("word/document.xml",new string('x',16*1024*1024+1))),".docx",ct)));
        test("PDF page provenance and empty scan notice",()=>
        {
            var builder=new PdfDocumentBuilder();var font=builder.AddStandard14Font(Standard14Font.Helvetica);
            builder.AddPage(PageSize.A4).AddText("Accounting schedule",12,new PdfPoint(30,700),font);builder.AddPage(PageSize.A4);
            var doc=extractor.Extract(builder.Build(),".pdf",ct);Equal("第1页",doc.Fragments[0].Location);Equal(true,doc.Fragments[0].Text.Contains("Accounting"));Equal(true,doc.Notice!.Contains("OCR"));
        });
        var root=Path.Combine(directory,"library");Directory.CreateDirectory(root);var privateDirectory=Path.Combine(root,"private");Directory.CreateDirectory(privateDirectory);
        var grant=new KnowledgeFolder(Guid.NewGuid().ToString(),root,true,true);var privateGrant=new KnowledgeFolder(Guid.NewGuid().ToString(),privateDirectory,true,false,true);
        var path=Path.Combine(root,"notes.txt");File.WriteAllText(path,"会计学习资料");var indexPath=Path.Combine(directory,"knowledge.db");var index=new KnowledgeIndex(indexPath);
        test("knowledge first search builds index",()=>{var result=index.Search([grant],"会计",ct).GetAwaiter().GetResult();Equal(1,result.Hits.Count);Equal(1,result.Index.Updated);});
        test("knowledge persisted index reused after reopening",()=>{var result=new KnowledgeIndex(indexPath).Search([grant],"会计",ct).GetAwaiter().GetResult();Equal(1,result.Index.Reused);Equal("第1行",result.Hits[0].Location);});
        test("knowledge detects edits with unchanged size and timestamp",()=>{var stamp=File.GetLastWriteTimeUtc(path);File.WriteAllText(path,"审计学习资料");File.SetLastWriteTimeUtc(path,stamp);var result=index.Search([grant],"审计",ct).GetAwaiter().GetResult();Equal(1,result.Index.Updated);Equal(1,result.Hits.Count);Equal(0,index.Search([grant],"会计",ct).GetAwaiter().GetResult().Hits.Count);});
        test("knowledge keyword SQL characters are literal",()=>Equal(0,index.Search([grant],"' OR 1=1 --",ct).GetAwaiter().GetResult().Hits.Count));
        test("cloud grants validated per source",()=>{var hit=index.Search([grant],"审计",ct).GetAwaiter().GetResult().Hits[0];KnowledgeIndex.ValidateForCloud([grant],[hit],ct).GetAwaiter().GetResult();Deny(()=>KnowledgeIndex.ValidateForCloud([grant with{AllowCloud=false}],[hit],ct).GetAwaiter().GetResult());});
        test("cloud refuses changed source after preview",()=>{var hit=index.Search([grant],"审计",ct).GetAwaiter().GetResult().Hits[0];File.WriteAllText(path,"已经改变");Deny(()=>KnowledgeIndex.ValidateForCloud([grant],[hit],ct).GetAwaiter().GetResult());});
        test("private child beats parent cloud permission",()=>Equal(false,KnowledgePolicy.CanSend([grant,privateGrant],grant.Id,Path.Combine(privateDirectory,"secret.txt"))));
        test("disabled private child still blocks parent cloud permission",()=>Equal(false,KnowledgePolicy.CanSend([grant,privateGrant with{Enabled=false}],grant.Id,Path.Combine(privateDirectory,"secret.txt"))));
        test("disabled child excludes parent indexed results",()=>{var secret=Path.Combine(privateDirectory,"secret.txt");File.WriteAllText(secret,"PrivateChildNeedle");Equal(1,index.Search([grant,privateGrant],"PrivateChildNeedle",ct).GetAwaiter().GetResult().Hits.Count);Equal(0,index.Search([grant,privateGrant with{Enabled=false}],"PrivateChildNeedle",ct).GetAwaiter().GetResult().Hits.Count);File.Delete(secret);});
        test("path sibling is outside authorized root",()=>Equal(false,KnowledgePolicy.Contains(root,root+"-other/secret.txt")));
        test("revoked root removes persisted chunks",()=>{index.Refresh([],false,ct).GetAwaiter().GetResult();using var db=new SqliteConnection($"Data Source={indexPath}");db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT count(*) FROM chunks";Equal(0L,(long)cmd.ExecuteScalar()!);});
        test("deleted source is removed on refresh",()=>{index.Search([grant],"改变",ct).GetAwaiter().GetResult();File.Delete(path);Equal(0,index.Search([grant],"改变",ct).GetAwaiter().GetResult().Hits.Count);});
        test("damaged document reports incomplete extraction",()=>{File.WriteAllText(Path.Combine(root,"broken.pdf"),"not a PDF");var result=index.Search([grant],"anything",ct).GetAwaiter().GetResult();Equal(1,result.Index.Issues.Count);});
        test("pre-cancelled index update aborts",()=>{using var cancel=new CancellationTokenSource();cancel.Cancel();try{index.Refresh([grant],true,cancel.Token).GetAwaiter().GetResult();throw new Exception("not cancelled");}catch(OperationCanceledException){}});
        test("private and cloud grant combination rejected",()=>Deny(()=>KnowledgePolicy.Validate([grant with{Private=true}])));
        if(!OperatingSystem.IsWindows())
        {
            test("knowledge rejects symlink ancestor above grant",()=>{var alias=Path.Combine(directory,"alias");Directory.CreateSymbolicLink(alias,root);Deny(()=>KnowledgeIndex.EnsureSafe(Path.Combine(alias,"private"),Path.Combine(alias,"private")));});
            test("knowledge skips symlink escape files",()=>{var outside=Path.Combine(directory,"outside-source.txt");File.WriteAllText(outside,"NeverReadOutside");File.CreateSymbolicLink(Path.Combine(root,"link.txt"),outside);Equal(0,index.Search([grant],"NeverReadOutside",ct).GetAwaiter().GetResult().Hits.Count);});
        }
    }
}
