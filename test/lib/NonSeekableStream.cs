using System;
using System.IO;

public class NonSeekableStream : Stream
{
    Stream m_stream;
    public NonSeekableStream(Stream baseStream)
    {
        m_stream = baseStream;
    }
    public override bool CanRead
    {
        get { return m_stream.CanRead; }
    }

    public override bool CanSeek
    {
        get { return false; }
    }

    public override bool CanWrite
    {
        get { return m_stream.CanWrite; }
    }

    public override void Flush()
    {
        m_stream.Flush();
    }

    public override long Length
    {
        get { throw new NotSupportedException(); }
    }

    public override long Position
    {
        get
        {
            return m_stream.Position;
        }
        set
        {
            throw new NotSupportedException();
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return m_stream.Read(buffer, offset, count);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotImplementedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        m_stream.Write(buffer, offset, count);
    }
}