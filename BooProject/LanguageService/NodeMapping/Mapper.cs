﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Boo.Lang.Compiler.Ast;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text;
using Boo.Lang.Parser;
using Hill30.BooProject.LanguageService.Colorizer;
using Boo.Lang.Compiler;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Text.Adornments;

namespace Hill30.BooProject.LanguageService.NodeMapping
{
    public class Mapper
    {
        private readonly Service service;
        private ITextBuffer buffer;
        private readonly int[][] positionMap;
        private readonly Dictionary<int, List<MappedNode>> nodeDictionary = new Dictionary<int, List<MappedNode>>();
        private readonly ITextSnapshot currentSnapshot;
        private int currentPos = 0;
        private readonly List<ClassificationSpan> classificationSpans = new List<ClassificationSpan>();
        public IList<ClassificationSpan> ClassificationSpans { get { return classificationSpans; } }

        public Mapper(Service service, ITextBuffer buffer, int tabSize)
        {
            this.service = service;
            this.buffer = buffer;
            currentSnapshot = buffer.CurrentSnapshot;
            var sourceText = currentSnapshot.GetText();

            var sourcePos = 0;
            var mappedPos = 0;
            var mappers = new List<int[]>();
            var positionList = new List<int>();
            foreach (var c in sourceText)
            {
                if (c == '\t')
                    while (mappedPos % tabSize < tabSize - 1)
                    {
                        positionList.Add(sourcePos);
                        mappedPos++;
                    }
                positionList.Add(sourcePos++);
                mappedPos++;
                if (c=='\n')
                {
                    mappers.Add(positionList.ToArray());
                    positionList.Clear();
                    sourcePos = 0;
                    mappedPos = 0;
                }
            }
            positionList.Add(sourcePos); // to map the <EOL> token
            mappers.Add(positionList.ToArray());
            positionMap = mappers.ToArray();
        }

        internal void MapToken(antlr.IToken token)
        {
            if (token.Type == BooLexer.EOL)
                return;

            if (token.Type == BooLexer.INDENT)
                return;

            if (token.Type == BooLexer.DEDENT)
                return;

            var start = positionMap[token.getLine() - 1][token.getColumn() - 1];

            var length = token.getText().Length;
            if (token.Type == BooLexer.TRIPLE_QUOTED_STRING)
                length += 6;
            if (token.Type == BooLexer.DOUBLE_QUOTED_STRING)
                length += 2;
            if (token.Type == BooLexer.SINGLE_QUOTED_STRING)
                length += 2;

            var end = positionMap[token.getLine() - 1][token.getColumn() - 1 + length];
            length = end - start;

            var span =
                new SnapshotSpan(currentSnapshot,
                    currentSnapshot.GetLineFromLineNumber(token.getLine() - 1).Start + start,
                    length);
            if (span.Start > currentPos)
                classificationSpans.Add(new ClassificationSpan
                    (new SnapshotSpan(currentSnapshot, currentPos, span.Start - currentPos),
                    service.ClassificationTypeRegistry.GetClassificationType(Formats.BooBlockComment)
                    ));

            var format = tokenFormats[(int)GetTokenType(token)];
            if (format != null)
            {
                classificationSpans.Add(new ClassificationSpan(span, service.ClassificationTypeRegistry.GetClassificationType(format)));
            }

            currentPos = span.End;

        }

        public void MapNode(MappedNode node)
        {
            List<MappedNode> nodes;
            if (!nodeDictionary.TryGetValue(node.Line-1, out nodes))
                nodeDictionary[node.Line-1] = nodes = new List<MappedNode>();
            nodes.Add(node);
        }

        public string FilePath
        {
            get
            {
                var doc = buffer.Properties[typeof(ITextDocument)] as ITextDocument;
                if (doc == null)
                    return null;
                return doc.FilePath;
            }
        }

        /// <summary>
        /// Produces a list of nodes for a given location (in the Boo Compiler format) filtered by the provided filter
        /// </summary>
        /// <param name="loc">Location in the source code as provided by Boo compiler</param>
        /// <param name="filter"></param>
        /// <returns></returns>
        /// <remarks> The text span for the selected nodes include the location provided</remarks>
        public IEnumerable<MappedNode> GetNodes(LexicalInfo loc, Func<MappedNode, bool> filter)
        {
            if (FilePath == loc.FileName)
                return GetNodes(loc.Line - 1, MapPosition(loc.Line, loc.Column), filter);
            var source = service.GetSource(loc.FullPath) as BooSource;
            if (source == null)
                return null;
            return source.Mapper.GetNodes(loc.Line - 1, source.Mapper.MapPosition(loc.Line, loc.Column), filter);
        }

        /// <summary>
        /// Produces a list of nodes for a given location (in the text buffer coordinates) filtered by the provided filter
        /// </summary>
        /// <param name="line">The 0 based line number </param>
        /// <param name="pos">The 0 based position number </param>
        /// <param name="filter"></param>
        /// <returns></returns>
        /// <remarks> The text span for the selected nodes include the location provided</remarks>
        public IEnumerable<MappedNode> GetNodes(int line, int pos, Func<MappedNode, bool> filter)
        {
            return GetNodes(line, pos, (node, li, po) => (po >= node.StartPos && po <= node.EndPos), filter);
        }

        /// <summary>
        /// Produces a list of nodes adjacent to a given location (in the text buffer coordinates) filtered by the provided filter
        /// </summary>
        /// <param name="line"></param>
        /// <param name="pos"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        /// <remarks> selects the closest nodes with the textspans ending before (to the left of) the location provided</remarks>
        internal IEnumerable<MappedNode> GetAdjacentNodes(int line, int pos, Func<MappedNode, bool> filter)
        {
            var list = GetNodes(line, pos, (node, li, po) => (po > node.EndPos), filter);
            var max = list.Max(item => item.EndPos);
            return list.Where(item => item.EndPos == max);
        }

        private IEnumerable<MappedNode> GetNodes(int line, int pos, Func<MappedNode, int, int, bool> comparer, Func<MappedNode, bool> filter)
        {
            List<MappedNode> nodes;
            if (!nodeDictionary.TryGetValue(line, out nodes))
                yield break;
            foreach (var node in nodes)
                if (filter(node) && comparer(node, line, pos))
                    yield return node;
        }

        internal int MapPosition(int line, int pos)
        {
            return positionMap[line - 1][pos - 1];
        }

        internal Tuple<int, int> MapSpan(int lineNo, int pos, int length)
        {
            return new Tuple<int, int>(
                MapPosition(lineNo,pos),
                MapPosition(lineNo, pos + length)
                );
        }

        internal void Complete()
        {
            foreach (var list in nodeDictionary.Values)
                foreach (var node in list)
                {
                    node.Resolve();
                    if (node.Format != null)
                    {
                        var start = currentSnapshot.GetLineFromLineNumber(node.Line - 1).Start + node.StartPos;
                        var span = new SnapshotSpan(currentSnapshot, start, node.EndPos - node.StartPos);
                        classificationSpans.Add(new ClassificationSpan(span,
                                                                       service.ClassificationTypeRegistry.GetClassificationType(
                                                                           node.Format)));
                    }
                }

            if (currentPos < currentSnapshot.Length-1)
                classificationSpans.Add(
                    new ClassificationSpan(
                        new SnapshotSpan(currentSnapshot, currentPos, currentSnapshot.Length - currentPos),
                        service.ClassificationTypeRegistry.GetClassificationType(Formats.BooBlockComment)
                        ));
        }

        internal SnapshotSpan SnapshotSpan { get { return new SnapshotSpan(currentSnapshot, 0, currentSnapshot.Length); } }


        internal SnapshotSpan GetSnapshotSpan(LexicalInfo lexicalInfo)
        {
            var line = currentSnapshot.GetLineFromLineNumber(lexicalInfo.Line - 1);
            var start = line.Start + MapPosition(lexicalInfo.Line, lexicalInfo.Column);
            var node = GetNodes(lexicalInfo, n => true).FirstOrDefault();
            if (node == null)
                return new SnapshotSpan(currentSnapshot, start,  line.End - start);
            else
                return new SnapshotSpan(currentSnapshot, start, node.Length);
        }

        internal CompilerErrorCollection Errors { get; set; }

        static readonly string[] tokenFormats = 
        {
            null,
            null,
            null,
            null,
            null,
            Formats.BooKeyword,
            null
        };

        public enum TokenType
        {
            DocumentString = 0,
            String = 1,
            MemberSelector = 2,
            WhiteSpace = 3,
            Identifier = 4,
            Keyword = 5,
            Other = 6,
        }

        public static TokenType GetTokenType(antlr.IToken token)
        {
            switch (token.Type)
            {
                case BooLexer.TRIPLE_QUOTED_STRING: return TokenType.DocumentString;

                case BooLexer.DOUBLE_QUOTED_STRING:
                case BooLexer.SINGLE_QUOTED_STRING:
                    return TokenType.String;

                case BooLexer.DOT: return TokenType.MemberSelector;

                case BooLexer.WS: return TokenType.WhiteSpace;

                case BooLexer.ID: return TokenType.Identifier;

                case BooLexer.ABSTRACT:
                case BooLexer.AS:
                case BooLexer.BREAK:
                case BooLexer.CLASS:
                case BooLexer.CONSTRUCTOR:
                case BooLexer.CONTINUE:
                case BooLexer.DEF:
                case BooLexer.DO:
                case BooLexer.ELIF:
                case BooLexer.ELSE:
                case BooLexer.ENUM:
                case BooLexer.EVENT:
                case BooLexer.EXCEPT:
                case BooLexer.FALSE:
                case BooLexer.FINAL:
                case BooLexer.FOR:
                case BooLexer.FROM:
                case BooLexer.GET:
                case BooLexer.GOTO:
                case BooLexer.IF:
                case BooLexer.IMPORT:
                case BooLexer.IN:
                case BooLexer.INTERFACE:
                case BooLexer.INTERNAL:
                case BooLexer.IS:
                case BooLexer.LONG:
                case BooLexer.NAMESPACE:
                case BooLexer.NULL:
                case BooLexer.OF:
                case BooLexer.OVERRIDE:
                case BooLexer.PARTIAL:
                case BooLexer.PASS:
                case BooLexer.PRIVATE:
                case BooLexer.PROTECTED:
                case BooLexer.PUBLIC:
                case BooLexer.RAISE:
                case BooLexer.REF:
                case BooLexer.RETURN:
                case BooLexer.SELF:
                case BooLexer.SET:
                case BooLexer.STATIC:
                case BooLexer.STRUCT:
                case BooLexer.SUPER:
                case BooLexer.THEN:
                case BooLexer.TRUE:
                case BooLexer.TRY:
                case BooLexer.TYPEOF:
                case BooLexer.UNLESS:
                case BooLexer.VIRTUAL:
                case BooLexer.WHILE:
                case BooLexer.YIELD:
                    return TokenType.Keyword;

                default: return TokenType.Other;
            }
        }
    }
}
