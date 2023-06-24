using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KalmiaZero.Utils
{
    public class Tokenizer
    {
        public int Pos { get; private set; }
        public bool IsEndOfString { get; private set; }

        public string Input
        {
            get => this.input;

            set
            {
                this.input = value;
                this.Pos = 0;
                this.IsEndOfString = false;
            }
        }

        readonly char[] DELIMITERS;   
        string input = string.Empty;

        public Tokenizer() => this.DELIMITERS = new char[] { ' ', '\t', '\r', '\n' };
        public Tokenizer(List<char> delimiters) => this.DELIMITERS = delimiters.ToArray();

        public char ReadNextChar()
        {
            SkipDelimiters();

            if (this.Pos == this.input.Length)
            {
                this.IsEndOfString = true;
                return '\0';
            }

            return this.input[this.Pos++];
        }

        public string ReadNext()
        {
            SkipDelimiters();

            if (this.Pos == this.input.Length)
            {
                this.IsEndOfString = true;
                return string.Empty;
            }

            int startPos = this.Pos;
            while (this.Pos < this.input.Length && !this.DELIMITERS.Contains(this.input[this.Pos]))
                this.Pos++;

            if (this.Pos == this.input.Length)
                this.IsEndOfString = true;

            return this.input[startPos..this.Pos];
        }

        public string ReadTo(char end)
        {
            SkipDelimiters();

            if (this.Pos == this.input.Length)
            {
                this.IsEndOfString = true;
                return string.Empty;
            }

            var span = this.Input.AsSpan(this.Pos);
            var idx = span.IndexOf(end);
            if (idx == -1)
            {
                this.IsEndOfString = true;
                this.Pos = this.Input.Length;
                return span[..span.Length].ToString();
            }

            this.Pos += idx + 1;
            return span[..idx].ToString();
        }

        void SkipDelimiters()
        {
            while (this.Pos < this.input.Length && this.DELIMITERS.Contains(this.input[this.Pos]))
                this.Pos++;
        }
    }
}
