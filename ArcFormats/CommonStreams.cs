//! \file       CommonStreams.cs
//! \date       Sun Mar 13 00:01:47 2016
//! \brief      Commonly used stream implementations.
//
// Copyright (C) 2016 by morkt
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to
// deal in the Software without restriction, including without limitation the
// rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
// sell copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS
// IN THE SOFTWARE.
//

using System;
using System.IO;
namespace GameRes.Formats
{
    /// <summary>
    /// Base class for various filter streams.
    /// </summary>
    public class ProxyStream : Stream
    {
        Stream      m_stream;
        bool        m_should_dispose;

        public ProxyStream (Stream input, bool leave_open = false)
        {
            m_stream = input;
            m_should_dispose = !leave_open;
        }

        public Stream BaseStream { get { return m_stream; } }

        public override bool CanRead  { get { return m_stream.CanRead; } }
        public override bool CanSeek  { get { return m_stream.CanSeek; } }
        public override bool CanWrite { get { return m_stream.CanWrite; } }
        public override long Length   { get { return m_stream.Length; } }
        public override long Position
        {
            get { return m_stream.Position; }
            set { m_stream.Position = value; }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            return m_stream.Read (buffer, offset, count);
        }

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            return m_stream.Seek (offset, origin);
        }

        public override void SetLength (long length)
        {
            m_stream.SetLength (length);
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            m_stream.Write (buffer, offset, count);
        }

        bool _proxy_disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (!_proxy_disposed)
            {
                if (m_should_dispose && disposing)
                    m_stream.Dispose();
                _proxy_disposed = true;
                base.Dispose (disposing);
            }
        }
    }

    public class PrefixStream : ProxyStream
    {
        byte[]  m_header;
        long    m_position = 0;

        public PrefixStream (byte[] header, Stream main, bool leave_open = false)
            : base (main, leave_open)
        {
            m_header = header;
        }

        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return BaseStream.Length + m_header.Length; } }
        public override long Position
        {
            get { return m_position; }
            set
            {
                if (!BaseStream.CanSeek)
                    throw new NotSupportedException ("Underlying stream does not support Stream.Position property");
                m_position = Math.Max (value, 0);
                if (m_position > m_header.Length)
                {
                    long stream_pos = BaseStream.Seek (m_position - m_header.Length, SeekOrigin.Begin);
                    m_position = m_header.Length + stream_pos;
                }
            }
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                Position = offset;
            else if (SeekOrigin.Current == origin)
                Position = m_position + offset;
            else
                Position = Length + offset;

            return m_position;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            if (m_position < m_header.Length)
            {
                int header_count = Math.Min (count, m_header.Length - (int)m_position);
                Buffer.BlockCopy (m_header, (int)m_position, buffer, offset, header_count);
                m_position += header_count;
                read += header_count;
                offset += header_count;
                count -= header_count;
            }
            if (count > 0)
            {
                if (m_header.Length == m_position && BaseStream.CanSeek)
                    BaseStream.Position = 0;
                int stream_read = BaseStream.Read (buffer, offset, count);
                m_position += stream_read;
                read += stream_read;
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (m_position < m_header.Length)
                return m_header[m_position++];
            if (m_position == m_header.Length && BaseStream.CanSeek)
                BaseStream.Position = 0;
            int b = BaseStream.ReadByte();
            if (-1 != b)
                m_position++;
            return b;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("PrefixStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("PrefixStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("PrefixStream.WriteByte method is not supported");
        }
    }

    /// <summary>
    /// Represents a region within existing stream.
    /// Underlying stream should allow seeking (CanSeek == true).
    /// </summary>
    public class StreamRegion : ProxyStream
    {
        private long    m_begin;
        private long    m_end;

        public StreamRegion (Stream main, long offset, long length, bool leave_open = false)
            : base (main, leave_open)
        {
            m_begin = offset;
            m_end = Math.Min (offset + length, BaseStream.Length);
            BaseStream.Position = m_begin;
        }

        public StreamRegion (Stream main, long offset, bool leave_open = false)
            : this (main, offset, main.Length-offset, leave_open)
        {
        }

        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length   { get { return m_end - m_begin; } }
        public override long Position
        {
            get { return BaseStream.Position - m_begin; }
            set { BaseStream.Position = Math.Max (m_begin + value, m_begin); }
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                offset += m_begin;
            else if (SeekOrigin.Current == origin)
                offset += BaseStream.Position;
            else
                offset += m_end;
            offset = Math.Max (offset, m_begin);
            BaseStream.Position = offset;
            return offset - m_begin;
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read = 0;
            long available = m_end - BaseStream.Position;
            if (available > 0)
            {
                read = BaseStream.Read (buffer, offset, (int)Math.Min (count, available));
            }
            return read;
        }

        public override int ReadByte ()
        {
            if (BaseStream.Position < m_end)
                return BaseStream.ReadByte();
            else
                return -1;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("StreamRegion.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("StreamRegion.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("StreamRegion.WriteByte method is not supported");
        }
    }

    /// <summary>
    /// Lazily evaluated wrapper around non-seekable stream.
    /// </summary>
    public class SeekableStream : Stream
    {
        Stream      m_source;
        Stream      m_buffer;
        bool        m_should_dispose;
        bool        m_source_depleted;
        long        m_read_pos;

        public SeekableStream (Stream input, bool leave_open = false)
        {
            m_source = input;
            m_should_dispose = !leave_open;
            m_read_pos = 0;
            if (m_source.CanSeek)
            {
                m_buffer = m_source;
                m_source_depleted = true;
            }
            else
            {
                m_buffer = new MemoryStream();
                m_source_depleted = false;
            }
        }

        #region IO.Stream Members
        public override bool CanRead  { get { return m_buffer.CanRead; } }
        public override bool CanSeek  { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length
        {
            get
            {
                if (!m_source_depleted)
                {
                    m_buffer.Seek (0, SeekOrigin.End);
                    m_source.CopyTo (m_buffer);
                    m_source_depleted = true;
                }
                return m_buffer.Length;
            }
        }
        public override long Position
        {
            get { return m_read_pos; }
            set { m_read_pos = value; }
        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int read, total_read = 0;
            if (m_source_depleted)
            {
                m_buffer.Position = m_read_pos;
                total_read = m_buffer.Read (buffer, offset, count);
                m_read_pos += total_read;
                return total_read;
            }
            if (m_read_pos < m_buffer.Length)
            {
                int available = (int)Math.Min (m_buffer.Length-m_read_pos, count);
                m_buffer.Position = m_read_pos;
                total_read = m_buffer.Read (buffer, offset, available);
                m_read_pos += total_read;
                count -= total_read;
                if (0 == count)
                    return total_read;
                offset += total_read;
            }
            else if (count > 0)
            {
                m_buffer.Seek (0, SeekOrigin.End);
                while (m_read_pos > m_buffer.Length)
                {
                    int available = (int)Math.Min (m_read_pos - m_buffer.Length, count);
                    read = m_source.Read (buffer, offset, available);
                    if (0 == read)
                    {
                        m_source_depleted = true;
                        return 0;
                    }
                    m_buffer.Write (buffer, offset, read);
                }
            }
            read = m_source.Read (buffer, offset, count);
            m_read_pos += read;
            m_buffer.Write (buffer, offset, read);
            return total_read + read;
        }

        public override void Flush()
        {
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            if (SeekOrigin.Begin == origin)
                m_read_pos = offset;
            else if (SeekOrigin.Current == origin)
                m_read_pos += offset;
            else
                m_read_pos = Length + offset;

            return m_read_pos;
        }

        public override void SetLength (long length)
        {
            throw new NotSupportedException ("SeekableStream.SetLength method is not supported");
        }

        public override void Write (byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException ("SeekableStream.Write method is not supported");
        }

        public override void WriteByte (byte value)
        {
            throw new NotSupportedException ("SeekableStream.WriteByte method is not supported");
        }
        #endregion

        #region IDisposable Members
        bool _disposed = false;
        protected override void Dispose (bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (m_should_dispose)
                    m_source.Dispose();
                if (m_buffer != m_source)
                    m_buffer.Dispose();
            }
            _disposed = true;
            base.Dispose (disposing);
        }
        #endregion
    }
}