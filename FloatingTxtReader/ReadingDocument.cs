namespace FloatingTxtReader;

internal sealed class ReadingDocument
{
    internal List<string> Lines { get; } = [];
    internal int BlankLineCount { get; private set; }
    internal string BeforePreview { get; private set; } = "";
    internal string AfterPreview { get; private set; } = "";
    private readonly List<int> _sourceOffsets = [];
    private readonly List<(int Start, int Count)> _blankRanges = [];

    internal static ReadingDocument Create(string content, int charactersPerLine, bool removeBlankLines = false)
    {
        var document = new ReadingDocument();
        charactersPerLine = Math.Max(1, charactersPerLine);
        var offset = 0;
        var previousOffset = -1;
        var previousLength = 0;
        List<(string Text, bool Blank)>? sample = null;
        var sampledBlankLines = 0;
        while (true)
        {
            var remaining = content.AsSpan(offset);
            var newline = remaining.IndexOfAny('\r', '\n');
            var sourceLine = newline < 0 ? remaining : remaining[..newline];
            var blank = sourceLine.Trim().IsEmpty;
            // A final newline terminates the previous line; it is not another physical blank line.
            if (blank && offset < content.Length)
            {
                document.BlankLineCount++;
                if (sample is null)
                {
                    sample = [];
                    if (previousOffset >= 0) sample.Add((Snippet(content.AsSpan(previousOffset, previousLength)), false));
                }
            }
            if (sample is not null && sample.Count < 8 && (!blank || sampledBlankLines < 2))
            {
                sample.Add((blank ? "" : Snippet(sourceLine), blank));
                if (blank) sampledBlankLines++;
            }
            if (sample is null && !blank)
            {
                previousOffset = offset;
                previousLength = sourceLine.Length;
            }
            if (!removeBlankLines || !blank)
            {
                var start = document.Lines.Count;
                if (sourceLine.IsEmpty)
                {
                    document.Lines.Add("");
                    document._sourceOffsets.Add(offset);
                }
                for (var index = 0; index < sourceLine.Length; index += charactersPerLine)
                {
                    document.Lines.Add(sourceLine.Slice(index, Math.Min(charactersPerLine, sourceLine.Length - index)).ToString());
                    document._sourceOffsets.Add(offset + index);
                }
                if (blank) document._blankRanges.Add((start, document.Lines.Count - start));
            }
            if (newline < 0) break;
            offset += newline + (remaining[newline] == '\r' && newline + 1 < remaining.Length && remaining[newline + 1] == '\n' ? 2 : 1);
        }
        if (sample is not null)
        {
            document.BeforePreview = string.Join(Environment.NewLine, sample.Select(line => line.Text));
            document.AfterPreview = string.Join(Environment.NewLine, sample.Where(line => !line.Blank).Select(line => line.Text));
        }
        document.EnsurePlaceholder();
        return document;
    }

    internal void RemoveBlankLines()
    {
        var read = 0;
        var write = 0;
        foreach (var (start, count) in _blankRanges)
        {
            while (read < start)
            {
                Lines[write] = Lines[read];
                _sourceOffsets[write++] = _sourceOffsets[read++];
            }
            read = start + count;
        }
        while (read < Lines.Count)
        {
            Lines[write] = Lines[read];
            _sourceOffsets[write++] = _sourceOffsets[read++];
        }
        Lines.RemoveRange(write, Lines.Count - write);
        _sourceOffsets.RemoveRange(write, _sourceOffsets.Count - write);
        _blankRanges.Clear();
        EnsurePlaceholder();
    }

    internal int SourceOffset(int line) => _sourceOffsets[Math.Clamp(line, 0, _sourceOffsets.Count - 1)];

    internal int FindLine(int sourceOffset)
    {
        var index = _sourceOffsets.BinarySearch(Math.Max(0, sourceOffset));
        if (index >= 0) return index;
        index = ~index;
        if (index > 0 && sourceOffset < _sourceOffsets[index - 1] + Lines[index - 1].Length) return index - 1;
        return Math.Min(index, Lines.Count - 1);
    }

    private void EnsurePlaceholder()
    {
        if (Lines.Count != 0) return;
        Lines.Add("");
        _sourceOffsets.Add(0);
    }

    private static string Snippet(ReadOnlySpan<char> text) => text.Length <= 32 ? text.ToString() : text[..32].ToString() + "…";
}
