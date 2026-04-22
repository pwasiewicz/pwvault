using PwVault.Core.Domain;

namespace PwVault.Web.Components.Shared;

public sealed class TreeNode
{
    public string Label { get; }
    public string FullPath { get; }
    public StoredEntry? Leaf { get; }
    public List<TreeNode> Children { get; } = new();

    public bool IsLeaf => Leaf is not null;

    public TreeNode(string label, string fullPath, StoredEntry? leaf)
    {
        Label = label;
        FullPath = fullPath;
        Leaf = leaf;
    }

    public static TreeNode BuildForest(IEnumerable<StoredEntry> entries)
    {
        var root = new TreeNode("", "", null);
        foreach (var stored in entries)
        {
            var segments = stored.Entry.Path.Segments;
            var current = root;
            var accumulated = "";

            for (var i = 0; i < segments.Count; i++)
            {
                accumulated = accumulated.Length == 0 ? segments[i] : accumulated + "/" + segments[i];
                var isLeaf = i == segments.Count - 1;

                TreeNode? existing = null;
                foreach (var child in current.Children)
                {
                    if (child.Label == segments[i] && child.IsLeaf == isLeaf)
                    {
                        existing = child;
                        break;
                    }
                }

                if (existing is null)
                {
                    existing = new TreeNode(segments[i], accumulated, isLeaf ? stored : null);
                    current.Children.Add(existing);
                }

                current = existing;
            }
        }
        return root;
    }
}
