// GameLog — the log panel on the right, and the lines in it.
//
// Every part of the view writes here: the narrator (what happened each phase), the game setup, test and sim
// results, warnings about seats. Lines are rich text (bold, colours, sizes). Add() keeps the newest line in view.
// PlainText() strips the tags for the transcript file. In a browser, fancy punctuation is made plain on the way in.

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Mandate.Viewer
{
    public sealed class GameLog
    {
        const int MaxLines = 600;   // older lines are dropped so a long AI game does not slow the GUI

        public readonly List<string> Lines = new List<string>();
        Vector2 scroll;

        /// <summary>Appends one rich-text line and scrolls to it.</summary>
        public void Add(string line)
        {
            Lines.Add(WebFiles.InBrowser ? PlainPunctuation(line) : line);
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
            scroll.y = float.MaxValue;
        }

        public void Clear() => Lines.Clear();

        // The web build's built-in font only has basic Latin letters, and AI models love curly quotes and dashes,
        // which would show as blanks. Swapped for plain ones as lines arrive (the transcript's raw replies keep them).
        static string PlainPunctuation(string s) => s
            .Replace('‘', '\'').Replace('’', '\'').Replace('“', '"').Replace('”', '"')
            .Replace("—", " - ").Replace('–', '-').Replace("…", "...").Replace('•', '*')
            .Replace("→", "->").Replace("←", "<-").Replace(' ', ' ');

        /// <summary>Every line with the rich-text tags removed, one per line.</summary>
        public string PlainText()
        {
            var sb = new StringBuilder();
            foreach (var line in Lines) sb.AppendLine(Regex.Replace(line, "<.*?>", ""));
            return sb.ToString();
        }

        /// <summary>Draws the log panel down the right-hand side of the screen.</summary>
        public void Draw()
        {
            GUILayout.BeginArea(new Rect(Ui.CanvasW - Ui.RightW, Ui.TopH, Ui.RightW, Ui.CanvasH - Ui.TopH), Ui.Panel);
            GUILayout.Label("<b>Log</b>", Ui.Label);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var line in Lines) GUILayout.Label(line, Ui.LogLine);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
