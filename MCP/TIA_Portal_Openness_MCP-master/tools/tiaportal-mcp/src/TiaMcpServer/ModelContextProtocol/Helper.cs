using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using System;
using System.Collections.Generic;

namespace TiaMcpServer.ModelContextProtocol
{
    public class Helper
    {
        public static List<Attribute> GetAttributeList(IEngineeringObject obj)
        {
            var attributes = new List<Attribute>();

            if (obj != null)
            {
                foreach (var attr in obj.GetAttributeInfos())
                {
                    object value;
                    try { value = obj.GetAttribute(attr.Name); }
                    catch (Exception ex) { value = $"<unreadable: {ex.GetType().Name}>"; }
                    attributes.Add(new Attribute
                    {
                        Name = attr.Name,
                        Value = ToSerializableValue(value),
                        AccessMode = Enum.GetName(typeof(EngineeringAttributeAccessMode), attr.AccessMode)
                    });
                }
            }

            return attributes;
        }

        // Convert non-primitive attribute values to a safe representation to avoid JSON cycle errors
        // (e.g. CultureInfo.Parent.Parent.Parent... chain triggers JsonException at depth 64).
        private static object ToSerializableValue(object value)
        {
            if (value == null) return null!;
            var t = value.GetType();
            if (t.IsPrimitive || value is string || value is decimal || value is DateTime || value is TimeSpan || value is Guid || t.IsEnum)
                return value;
            // Anything else (CultureInfo, Siemens engineering objects, etc.) → string form
            try { return value.ToString(); }
            catch { return $"<{t.Name}>"; }
        }

        public static BlockGroupInfo BuildBlockHierarchy(PlcBlockGroup group)
        {
            var groupInfo = new BlockGroupInfo
            {
                Name = group.Name
            };

            var blockList = new List<ResponseBlockInfo>();
            foreach (var block in group.Blocks)
            {
                var attributes = Helper.GetAttributeList(block);
                blockList.Add(new ResponseBlockInfo
                {
                    Name = block.Name,
                    TypeName = block.GetType().Name,
                    Namespace = block.Namespace,
                    ProgrammingLanguage = Enum.GetName(typeof(ProgrammingLanguage), block.ProgrammingLanguage),
                    MemoryLayout = Enum.GetName(typeof(MemoryLayout), block.MemoryLayout),
                    IsConsistent = block.IsConsistent,
                    HeaderName = block.HeaderName,
                    ModifiedDate = block.ModifiedDate,
                    IsKnowHowProtected = block.IsKnowHowProtected,
                    Attributes = attributes,
                    Description = block.ToString()
                });
            }
            groupInfo.Blocks = blockList;

            var groupList = new List<BlockGroupInfo>();
            foreach (var subGroup in group.Groups)
            {
                groupList.Add(BuildBlockHierarchy(subGroup));
            }
            groupInfo.Groups = groupList;

            return groupInfo;
        }
    }
}
