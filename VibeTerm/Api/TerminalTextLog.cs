using System;
using System.Collections.Generic;
using System.Text;

namespace VibeTerm.Api;

/// <summary>
/// Turns raw terminal bytes into readable text and keeps a rolling offset.
/// Color codes are removed. Carriage return and erase-to-end apply to the
/// current line, so a shell prompt stays readable. Cursor-up is ignored, so
/// earlier lines are not rewritten by full-screen programs.
/// </summary>
public sealed class TerminalTextLog
{
    private const int MaxCommittedChars = 256 * 1024;
    private const int MaxLineChars = 4000;

    private enum State
    {
        Text,
        Escape,
        Csi,
        StringSeq,
        Charset
    }

    private readonly object _gate = new();
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly StringBuilder _committed = new();
    private readonly StringBuilder _line = new();
    private readonly List<int> _csiParams = new();
    private State _state = State.Text;
    private bool _stringEsc;
    private int _csiCurrent = -1;
    private bool _csiPrivate;
    private int _column;
    private long _baseOffset;

    public long NextOffset
    {
        get
        {
            lock (_gate)
                return _baseOffset + _committed.Length + _line.Length;
        }
    }

    public void Append(byte[] data)
    {
        if (data.Length == 0)
            return;

        lock (_gate)
        {
            char[] chars = new char[Encoding.UTF8.GetMaxCharCount(data.Length)];
            _decoder.Convert(
                data, 0, data.Length,
                chars, 0, chars.Length,
                false,
                out _, out int charsUsed, out _);

            for (int i = 0; i < charsUsed; i++)
                Feed(chars[i]);

            Trim();
        }
    }

    public TextChunk Read(long since, int maxChars)
    {
        if (since < 0)
            throw new ArgumentOutOfRangeException(nameof(since));
        if (maxChars < 1)
            throw new ArgumentOutOfRangeException(nameof(maxChars));

        lock (_gate)
        {
            string current = _committed.ToString() + _line.ToString();
            long end = _baseOffset + current.Length;
            if (since >= end)
                return new TextChunk(end, end, string.Empty, false);

            long start = since;
            bool truncated = false;
            if (start < _baseOffset)
            {
                start = _baseOffset;
                truncated = true;
            }

            int index = (int)(start - _baseOffset);
            int available = current.Length - index;
            int take = Math.Min(maxChars, available);
            if (take < available)
                truncated = true;

            return new TextChunk(start, start + take, current.Substring(index, take), truncated);
        }
    }

    private void Feed(char c)
    {
        switch (_state)
        {
            case State.Escape:
                FeedEscape(c);
                break;
            case State.Csi:
                FeedCsi(c);
                break;
            case State.StringSeq:
                FeedString(c);
                break;
            case State.Charset:
                _state = State.Text;
                break;
            default:
                FeedText(c);
                break;
        }
    }

    private void FeedText(char c)
    {
        switch (c)
        {
            case '\n':
                _committed.Append(_line);
                _committed.Append('\n');
                _line.Clear();
                _column = 0;
                return;
            case '\r':
                _column = 0;
                return;
            case '\b':
                if (_column > 0)
                    _column--;
                return;
            case '\t':
                WriteTab();
                return;
            case '\u001b':
                _state = State.Escape;
                return;
            default:
                if (c < ' ' || c == '\u007f')
                    return;
                WriteChar(c);
                return;
        }
    }

    private void FeedEscape(char c)
    {
        switch (c)
        {
            case '[':
                _state = State.Csi;
                _csiParams.Clear();
                _csiCurrent = -1;
                _csiPrivate = false;
                return;
            case ']':
            case 'P':
            case 'X':
            case '^':
            case '_':
                _state = State.StringSeq;
                _stringEsc = false;
                return;
            case '(':
            case ')':
            case '*':
            case '+':
                _state = State.Charset;
                return;
            default:
                _state = State.Text;
                return;
        }
    }

    private void FeedString(char c)
    {
        if (_stringEsc)
        {
            _stringEsc = false;
            if (c == '\\')
                _state = State.Text;
            return;
        }

        if (c == '\a')
        {
            _state = State.Text;
            return;
        }

        if (c == '\u001b')
            _stringEsc = true;
    }

    private void FeedCsi(char c)
    {
        if (c is '?' or '>' or '!' or '=')
        {
            _csiPrivate = true;
            return;
        }

        if (c is >= '0' and <= '9')
        {
            if (_csiCurrent < 0)
                _csiCurrent = 0;
            _csiCurrent = Math.Min(9999, _csiCurrent * 10 + (c - '0'));
            return;
        }

        if (c == ';')
        {
            _csiParams.Add(_csiCurrent < 0 ? 0 : _csiCurrent);
            _csiCurrent = -1;
            return;
        }

        if (c is >= ' ' and <= '/')
            return;

        if (c is >= '@' and <= '~')
        {
            _csiParams.Add(_csiCurrent < 0 ? 0 : _csiCurrent);
            if (!_csiPrivate)
                ApplyCsi(c);
            _state = State.Text;
            return;
        }

        _state = State.Text;
    }

    private void ApplyCsi(char command)
    {
        int arg = _csiParams.Count == 0 ? 0 : _csiParams[0];
        switch (command)
        {
            case 'K':
                EraseLine(arg);
                break;
            case 'G':
                _column = Math.Clamp(Math.Max(arg, 1) - 1, 0, MaxLineChars);
                break;
            case 'C':
                _column = Math.Min(MaxLineChars, _column + Math.Max(arg, 1));
                break;
            case 'D':
                _column = Math.Max(0, _column - Math.Max(arg, 1));
                break;
        }
    }

    private void EraseLine(int mode)
    {
        switch (mode)
        {
            case 1:
                int count = Math.Min(_column, _line.Length);
                for (int i = 0; i < count; i++)
                    _line[i] = ' ';
                break;
            case 2:
                _line.Clear();
                _column = 0;
                break;
            default:
                if (_column < _line.Length)
                    _line.Remove(_column, _line.Length - _column);
                break;
        }
    }

    private void WriteChar(char c)
    {
        if (_column >= MaxLineChars)
            return;

        while (_line.Length < _column)
            _line.Append(' ');

        if (_column < _line.Length)
            _line[_column] = c;
        else
            _line.Append(c);

        _column++;
    }

    private void WriteTab()
    {
        int spaces = 8 - (_column % 8);
        if (spaces == 0)
            spaces = 8;

        for (int i = 0; i < spaces; i++)
            WriteChar(' ');
    }

    private void Trim()
    {
        if (_committed.Length <= MaxCommittedChars)
            return;

        int extra = _committed.Length - MaxCommittedChars;
        int cut = extra;
        int limit = Math.Min(_committed.Length, extra + 8192);
        for (int i = extra; i < limit; i++)
        {
            if (_committed[i] == '\n')
            {
                cut = i + 1;
                break;
            }
        }

        _committed.Remove(0, cut);
        _baseOffset += cut;
    }
}
