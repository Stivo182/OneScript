/*----------------------------------------------------------
This Source Code Form is subject to the terms of the 
Mozilla Public License, v.2.0. If a copy of the MPL 
was not distributed with this file, You can obtain one 
at http://mozilla.org/MPL/2.0/.
----------------------------------------------------------*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace OneScript.StandardLibrary
{
    public class CustomLineFeedStreamReader : IDisposable
    {
        private TextReader _reader;
        private readonly string _eolDelimiter;
        private readonly char[] _buffer;
        private int _bufferPosition;
        private int _bufferLength;
        private bool _analyzeDefaults;

        private const int DefaultBufferSize = 4096;

        public CustomLineFeedStreamReader(TextReader underlyingReader, string eolDelimiter, bool analyzeDefaults)
        {
            _reader = underlyingReader ?? throw new ArgumentNullException(nameof(underlyingReader));
            _eolDelimiter = eolDelimiter ?? throw new ArgumentNullException(nameof(eolDelimiter));
            
            _buffer = new char[Math.Max(DefaultBufferSize, eolDelimiter.Length * 2)];
            _bufferPosition = 0;
            _bufferLength = 0; 
            _analyzeDefaults = analyzeDefaults;
        }

        private void EnsureBuffer(int minimalLength)
        {
            if (_bufferPosition + minimalLength <= _bufferLength)
                return;

            // Сдвигаем оставшиеся символы в начало буфера
            if (_bufferPosition > 0)
            {
                Array.Copy(_buffer, _bufferPosition, _buffer, 0, _bufferLength - _bufferPosition);
                _bufferLength -= _bufferPosition;
                _bufferPosition = 0;
            }

            // Читаем новые символы
            while (_bufferLength < _buffer.Length && _bufferLength < minimalLength)
            {
                int readCount = _reader.Read(_buffer, _bufferLength, _buffer.Length - _bufferLength);
                if (readCount == 0) break;
                _bufferLength += readCount;
            }
        }

        public int Read()
        {
            if (_bufferPosition >= _bufferLength)
            {
                EnsureBuffer(1);
                if (_bufferPosition >= _bufferLength)
                    return -1;
            }

            char currentChar = _buffer[_bufferPosition];

            // Обработка стандартных разделителей строк
            if (_analyzeDefaults && currentChar == '\r')
            {
                _bufferPosition++;
                EnsureBuffer(1);

                if (_bufferPosition < _bufferLength && _buffer[_bufferPosition] == '\n')
                {
                    _bufferPosition++;
                    return '\n';
                }
                return '\n';
            }

            // Проверка пользовательского разделителя
            if (_eolDelimiter.Length > 0 && currentChar == _eolDelimiter[0])
            {
                if (CheckEolDelimiter())
                {
                    return '\n';
                }
            }

            _bufferPosition++;
            return currentChar;
        }

        private bool CheckEolDelimiter()
        {
            EnsureBuffer(_eolDelimiter.Length);

            if (_bufferPosition + _eolDelimiter.Length > _bufferLength)
                return false;

            for (int i = 0; i < _eolDelimiter.Length; i++)
            {
                if (_buffer[_bufferPosition + i] != _eolDelimiter[i])
                    return false;
            }

            _bufferPosition += _eolDelimiter.Length;
            return true;
        }

        public string ReadUntil(string endOfString, out bool eosMet)
        {   
            if (string.IsNullOrEmpty(endOfString))
            {
                eosMet = false;
                return ReadToEnd();
            }

            var sb = new StringBuilder();
            eosMet = false;

            while (!eosMet)
            {
                int ic = Read();
                if (ic == -1) break;

                char c = (char)ic;
                sb.Append(c);

                // Оптимизированная проверка окончания строки
                if (c == endOfString[endOfString.Length - 1] && sb.Length >= endOfString.Length)
                {
                    if (EndsWith(sb, endOfString))
                    {
                        eosMet = true;
                        sb.Length -= endOfString.Length;
                    }
                }
            }

            return sb.Length == 0 && !eosMet ? null : sb.ToString();
        }

        private bool EndsWith(StringBuilder sb, string endString)
        {
            int startIndex = sb.Length - endString.Length;
            for (int i = 0; i < endString.Length; i++)
            {
                if (sb[startIndex + i] != endString[i])
                    return false;
            }
            return true;
        }

        public string ReadLine(string lineDelimiter)
        {
            bool eol;
            return ReadUntil(lineDelimiter, out eol);
        }

        public string ReadToEnd()
        {           
            var sb = new StringBuilder();
            int ch;
            while ((ch = Read()) != -1)
            {
                sb.Append((char)ch);
            }
            return sb.ToString();
        }

        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
        }
    }
}
