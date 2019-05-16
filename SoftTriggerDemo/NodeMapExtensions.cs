using Sentech.GenApiDotNET;

namespace SoftTriggerDemo
{
    internal static class NodeMapExtensions
    {
        public static bool SetEnumValue(this INodeMap nodeMap, string nodeName, string value)
        {
            var node = nodeMap.GetNode<IEnum>(nodeName);
            if (node == null || !node.IsWritable)
            {
                return false;
            }

            var entry = node.GetEntryNode(value);
            if (entry == null)
            {
                return false;
            }

            node.IntValue = entry.Value;

            return true;
        }
    }
}