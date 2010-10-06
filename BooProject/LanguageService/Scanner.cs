﻿using System;
using Microsoft.VisualStudio.Package;
using Boo.Lang.Parser;
using System.IO;
using Microsoft.VisualStudio.TextManager.Interop;

namespace Hill30.BooProject.LanguageService
{
    class Scanner : IScanner
    {
        antlr.TokenStream lexer;
        antlr.IToken stashedToken;
        int offset;
        int current;
        private Service service;
        private IVsTextLines buffer;

        private Source source;

        public Scanner(Source source)
        {
            this.source = source;
        }

        public Scanner(Service service, IVsTextLines buffer)
        {
            this.service = service;
            this.buffer = buffer;
        }

        #region IScanner Members

        public bool ScanTokenAndProvideInfoAboutIt(TokenInfo tokenInfo, ref int state)
        {
            var token = stashedToken;
            stashedToken = null;
            if (token == null)
                try
                {
                    token = lexer.nextToken();
                }
                catch (Exception)
                {
                    return false;
                }

            if (current < token.getColumn() - 1)
            {
                tokenInfo.StartIndex = current;
                tokenInfo.EndIndex = offset + token.getColumn() - 2;
                tokenInfo.Type = TokenType.Comment;
                tokenInfo.Color = TokenColor.Comment;
                current = tokenInfo.EndIndex + 1;
                stashedToken = token;
                return true;
            }

            if (token.Type == BooLexer.EOL || token.Type == BooLexer.EOF)
                return false;

            int quotes = 0;
            if (IsBlockComment(token))
            {
                tokenInfo.Type = TokenType.Comment;
                tokenInfo.Color = TokenColor.Comment;
            }
            else
                switch (token.Type)
                {
                    //case BooLexer.EOL:
                    //case BooLexer.EOF:
                    //    return false;

                    case BooLexer.TRIPLE_QUOTED_STRING:
                        quotes = 6;
                        tokenInfo.Type = TokenType.String;
                        tokenInfo.Color = TokenColor.String;
                        break;

                    case BooLexer.DOUBLE_QUOTED_STRING:
                    case BooLexer.SINGLE_QUOTED_STRING:
                        quotes = 2;
                        tokenInfo.Type = TokenType.String;
                        tokenInfo.Color = TokenColor.String;
                        break;

                    case BooLexer.WS:
                        tokenInfo.Type = TokenType.WhiteSpace;
                        tokenInfo.Color = TokenColor.Text;
                        break;

                    case BooLexer.ID:
                        tokenInfo.Type = TokenType.Identifier;
                        tokenInfo.Color = TokenColor.Identifier;
                        break;

                    case BooLexer.PUBLIC:
                    case BooLexer.DEF:
                    case BooLexer.CLASS:
                    case BooLexer.IMPORT:
                    case BooLexer.NAMESPACE:
                        tokenInfo.Type = TokenType.Keyword;
                        tokenInfo.Color = TokenColor.Keyword;
                        break;

                    default:
                        tokenInfo.Color = TokenColor.Text;
                        break;
                }

            tokenInfo.StartIndex = offset + token.getColumn() - 1;
            tokenInfo.EndIndex = offset + quotes + token.getColumn() - 1 + token.getText().Length - 1;
            current = tokenInfo.EndIndex + 1;
            return true;
        }

// ReSharper disable ParameterHidesMember
        public void SetSource(string source, int offset)
// ReSharper restore ParameterHidesMember
        {
            current = this.offset = offset;
            lexer = BooParser.CreateBooLexer(1, "Line Scanner", new StringReader(source.Substring(offset)));
        }

        #endregion

        int lineNumber = -1;
        internal void SetLineNumber(int line)
        {
            lineNumber = line;
            if (source == null)
                source = service.GetSource(buffer);
        }

        private bool IsBlockComment(antlr.IToken token)
        {
            if (source == null)
                return false;
            return ((BooSource)source).IsBlockComment(token);
        }

    }
}
