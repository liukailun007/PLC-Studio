using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace TiaMcpServer.ModelContextProtocol
{
    /// <summary>
    /// LAD FC 块 XML 组合器：把一个或多个 FlgNet/v5 网络包成完整的 LAD FC 块。
    /// 与 SCL FC 不同，编程语言是 LAD，CompileUnit 的 NetworkSource 内含 FlgNet（非 StructuredText）。
    /// </summary>
    public static class PlcLadFcBlockXmlComposer
    {
        private static readonly XNamespace InterfaceNs = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

        /// <summary>每个网络的描述：FlgNet 元素 + 可选中文标题/注释。</summary>
        public sealed class LadNetwork
        {
            public LadNetwork(XElement flgNet, string titleZhCn = "", string commentZhCn = "")
            {
                FlgNet = flgNet ?? throw new ArgumentNullException(nameof(flgNet));
                TitleZhCn = titleZhCn ?? "";
                CommentZhCn = commentZhCn ?? "";
            }
            public XElement FlgNet { get; }
            public string TitleZhCn { get; }
            public string CommentZhCn { get; }
        }

        public static XDocument Compose(
            string blockName,
            int blockNumber,
            IEnumerable<PlcBlockMemberDefinition> inputMembers,
            IEnumerable<PlcBlockMemberDefinition> outputMembers,
            IEnumerable<LadNetwork> networks,
            string blockCommentZhCn = "",
            string blockTitleZhCn = "")
        {
            if (string.IsNullOrWhiteSpace(blockName))
                throw new ArgumentException("LAD FC 块名称不能为空。", nameof(blockName));
            if (blockNumber <= 0)
                throw new ArgumentException("LAD FC 块编号必须大于 0。", nameof(blockNumber));

            var inputs = inputMembers?.ToArray() ?? Array.Empty<PlcBlockMemberDefinition>();
            var outputs = outputMembers?.ToArray() ?? Array.Empty<PlcBlockMemberDefinition>();
            var nets = networks?.ToArray() ?? throw new ArgumentNullException(nameof(networks));
            if (nets.Length == 0)
                throw new ArgumentException("LAD FC 至少需要 1 个网络。", nameof(networks));

            // ID 分配：1=Comment, 2=Title-Item, 3..=CompileUnits（每个 +3 ID 给 Comment/Title 子项），
            //         最后块级 Title。简化为单调递增的字符串 ID（hex 让 TIA 习惯）
            int idCounter = 1;
            string NextId() => idCounter++.ToString("X");

            var blockObjList = new XElement("ObjectList");
            blockObjList.Add(PlcBlockXmlHelpers.BuildMultilingualText(NextId(), NextId(), "Comment", blockCommentZhCn));

            for (var i = 0; i < nets.Length; i++)
            {
                var net = nets[i];
                var compileUnitId = NextId();
                var netCommentId = NextId();
                var netCommentItemId = NextId();
                var netTitleId = NextId();
                var netTitleItemId = NextId();

                var compileUnitObjList = new XElement("ObjectList",
                    PlcBlockXmlHelpers.BuildMultilingualText(netCommentId, netCommentItemId, "Comment", net.CommentZhCn),
                    PlcBlockXmlHelpers.BuildMultilingualText(netTitleId, netTitleItemId, "Title", net.TitleZhCn));

                var compileUnit = new XElement("SW.Blocks.CompileUnit",
                    new XAttribute("ID", compileUnitId),
                    new XAttribute("CompositionName", "CompileUnits"),
                    new XElement("AttributeList",
                        new XElement("NetworkSource", net.FlgNet),
                        new XElement("ProgrammingLanguage", "LAD")),
                    compileUnitObjList);

                blockObjList.Add(compileUnit);
            }

            blockObjList.Add(PlcBlockXmlHelpers.BuildMultilingualText(NextId(), NextId(), "Title", blockTitleZhCn));

            return new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("Document",
                    new XElement("Engineering", new XAttribute("version", "V21")),
                    new XElement("DocumentInfo",
                        new XElement("Created", "2000-01-01T00:00:00.0000000Z"),
                        new XElement("ExportSetting", "None"),
                        new XElement("InstalledProducts")),
                    new XElement("SW.Blocks.FC",
                        new XAttribute("ID", "0"),
                        new XElement("AttributeList",
                            new XElement("Interface",
                                new XElement(InterfaceNs + "Sections",
                                    BuildSection("Input", inputs),
                                    BuildSection("Output", outputs),
                                    BuildSection("InOut", Array.Empty<PlcBlockMemberDefinition>()),
                                    BuildSection("Temp", Array.Empty<PlcBlockMemberDefinition>()),
                                    BuildSection("Constant", Array.Empty<PlcBlockMemberDefinition>()),
                                    BuildSection("Return", new[] { new PlcBlockMemberDefinition("Ret_Val", "Void") }))),
                            new XElement("MemoryLayout", "Optimized"),
                            new XElement("Name", blockName),
                            new XElement("Namespace"),
                            new XElement("Number", blockNumber),
                            new XElement("ProgrammingLanguage", "LAD"),
                            new XElement("SetENOAutomatically", "false")),
                        blockObjList)));
        }

        public static string ComposeXml(
            string blockName,
            int blockNumber,
            IEnumerable<PlcBlockMemberDefinition> inputMembers,
            IEnumerable<PlcBlockMemberDefinition> outputMembers,
            IEnumerable<LadNetwork> networks,
            string blockCommentZhCn = "",
            string blockTitleZhCn = "")
        {
            using var writer = new Utf8StringWriter();
            Compose(blockName, blockNumber, inputMembers, outputMembers, networks, blockCommentZhCn, blockTitleZhCn).Save(writer, SaveOptions.None);
            return writer.ToString();
        }

        private static XElement BuildSection(string name, IReadOnlyCollection<PlcBlockMemberDefinition> members)
        {
            if (members.Count == 0)
                return new XElement(InterfaceNs + "Section", new XAttribute("Name", name));
            var section = new XElement(InterfaceNs + "Section", new XAttribute("Name", name));
            foreach (var m in members)
            {
                var memEl = new XElement(InterfaceNs + "Member",
                    new XAttribute("Name", m.Name),
                    new XAttribute("Datatype", m.Datatype));
                PlcBlockXmlHelpers.AppendMemberCommentIfAny(memEl, m.CommentZhCn);
                section.Add(memEl);
            }
            return section;
        }

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
