using OclUdp.Consts;
using OclUdp.Enums;
using OclUdp.Internals;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

namespace OclUdp.Sockets
{
    public class OclUdpStream : Stream
    {
        private const int _receiveSendSocketBufferSize = 1048576;
        private const int _maxDatagramWindowSize = 500;
        private const int _sendHeaderLength = 5;
        private const int _maxSendDatagramLength = 508; 
        private const int _maxSendDatagramDataLength = _maxSendDatagramLength - _sendHeaderLength;

        private readonly byte[] _dataReceivedDatagram = new byte[] { (byte)StatusCode.DataReceived, 0x0, 0x0, 0x0, 0x0 };

        private readonly TimeSpan _dataSentWaitTime = new(0, 10, 0);
        private readonly TimeSpan _dataReceivedWaitTime = new(0, 0, 0, 0, 100);
        private const int _maxDataReceivedTimeoutCount = 5;

        private readonly UdpClient _udpClient;

        private readonly bool[] _windowState;
        private readonly byte[][] _windowBuffer;

        private readonly ArrayFragment<byte>[] _windowBufferReceiveAccessors;
        private readonly ReadOnlyMemory<byte>[] _windowBufferSendAccessors;

        private int _receiveSequenceNumber;
        private int _sendSequenceNumber;

        private Task<UdpReceiveResult> _dataReceivedResultTask;

        public IPEndPoint RemoteEndPoint { get => (IPEndPoint)_udpClient.Client.RemoteEndPoint; }

        public bool IsOpened { get; private set; }

        internal OclUdpStream(UdpClient udpClient)
        {
            _udpClient = udpClient;

            _udpClient.Client.ReceiveBufferSize = _receiveSendSocketBufferSize;
            _udpClient.Client.SendBufferSize = _receiveSendSocketBufferSize;

            IsOpened = true;

            _windowState = new bool[_maxDatagramWindowSize];
            _windowBuffer = new byte[_maxDatagramWindowSize][];

            _windowBufferReceiveAccessors = new ArrayFragment<byte>[_maxDatagramWindowSize];
            _windowBufferSendAccessors = new ReadOnlyMemory<byte>[_maxDatagramWindowSize];

            for (int i = 0; i < _maxDatagramWindowSize; i++)
            {
                byte[] buffer = new byte[_maxSendDatagramLength];
                _windowBuffer[i] = buffer;
                _windowBufferReceiveAccessors[i] = new ArrayFragment<byte>(buffer, 0, _maxSendDatagramDataLength);
                _windowBufferSendAccessors[i] = new ReadOnlyMemory<byte>(buffer, 0, _maxSendDatagramLength);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            ValidateBufferArguments(buffer, offset, count);

            try
            {
                return ReadInternalAsync(buffer, offset, count).Result;
            }
            catch
            {
                IsOpened = false;
                throw;
            }
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateBufferArguments(buffer, offset, count);

            try
            {
                return ReadInternalAsync(buffer, offset, count, cancellationToken);
            }
            catch
            {
                IsOpened = false;
                throw;
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ThrowIfDisposed();
            ValidateBufferArguments(buffer, offset, count);

            try
            {
                WriteInternalAsync(buffer, offset, count).Wait();
            } 
            catch
            {
                IsOpened = false;
                throw;
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            ValidateBufferArguments(buffer, offset, count);

            try
            {
                return WriteInternalAsync(buffer, offset, count, cancellationToken);
            }
            catch
            {
                IsOpened = false;
                throw;
            }
        }

        public override bool CanRead => true;

        public override bool CanWrite => true;

        public override bool CanSeek => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            _disposed = true;
            IsOpened = false;

            _udpClient.Dispose();

            GC.SuppressFinalize(this);
        }

        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RestoreWindowState()
        {
            for (int i = 0; i < _maxDatagramWindowSize; i++)
                _windowState[i] = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CalculateWindowEndSequenceNumber(int windowStartSequenceNumber)
        {
            if (int.MaxValue - _maxDatagramWindowSize < windowStartSequenceNumber)
                return 0;
            return windowStartSequenceNumber + _maxDatagramWindowSize;
        }

        private async Task<int> ReadInternalAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            RestoreWindowState();

            int windowStartSequenceNumber = _receiveSequenceNumber;
            int windowEndSequenceNumber = CalculateWindowEndSequenceNumber(windowStartSequenceNumber);
            int currentWindowIndex = 0, bufferIndex = offset, remainedCount = count, copyCount;

            int all = 0, need = 0;

            while (remainedCount > 0)
            {
                Task<UdpReceiveResult> resultTask = _udpClient.ReceiveAsync(cancellationToken).AsTask();
                Task delayTask = Task.Delay(_dataSentWaitTime, cancellationToken);
                Task<Task> whenAnyTask = Task.WhenAny(resultTask, delayTask);

                if (await whenAnyTask == delayTask)
                    throw new TimeoutException("Failed to receive data from the remote host in the allotted time.");

                UdpReceiveResult result = resultTask.Result;
                byte[] resultBuffer = result.Buffer;
                int bufferLength = resultBuffer.Length;

                if (bufferLength < DatagramLength.DataSentMinLength 
                    || bufferLength > _maxSendDatagramLength
                    || (StatusCode)resultBuffer[0] != StatusCode.DataSent)
                    continue;

                _dataReceivedDatagram[1] = resultBuffer[1];
                _dataReceivedDatagram[2] = resultBuffer[2];
                _dataReceivedDatagram[3] = resultBuffer[3];
                _dataReceivedDatagram[4] = resultBuffer[4];

                await _udpClient.SendAsync(_dataReceivedDatagram, cancellationToken);

                all++;

                int sequenceNumber = BitConverter.ToInt32(resultBuffer, 1);
                int windowIndex = sequenceNumber - windowStartSequenceNumber;

                //await Console.Out.WriteLineAsync($"Rem: {remainedCount}, Rec: {_receiveSequenceNumber}, Seq: {sequenceNumber}, Win: {windowIndex}, Cur: {currentWindowIndex}");

                if (windowIndex < 0 || sequenceNumber >= windowEndSequenceNumber || _windowState[windowIndex])
                    continue;

                need++;

                _windowState[windowIndex] = true;
                copyCount = bufferLength - DatagramLength.DataSentMinLength;

                ArrayFragment<byte> windowBufferReceiveAccessor = _windowBufferReceiveAccessors[windowIndex];
                windowBufferReceiveAccessor.Count = copyCount;

                if (currentWindowIndex != windowIndex)
                {
                    Buffer.BlockCopy(resultBuffer, DatagramLength.DataSentMinLength,
                        _windowBuffer[currentWindowIndex], 0, copyCount);
                    continue;
                }

                windowBufferReceiveAccessor.Array = resultBuffer;
                windowBufferReceiveAccessor.Offset = DatagramLength.DataSentMinLength;

                do
                {
                    ArrayFragment<byte> receiveBufferAccessor = _windowBufferReceiveAccessors[currentWindowIndex];
                    copyCount = Math.Min(remainedCount, receiveBufferAccessor.Count);

                    Buffer.BlockCopy(receiveBufferAccessor.Array, receiveBufferAccessor.Offset,
                        buffer, bufferIndex, copyCount);

                    bufferIndex += copyCount;
                    remainedCount -= copyCount;
                    receiveBufferAccessor.Count = _maxSendDatagramDataLength;
                    currentWindowIndex++;
                    _receiveSequenceNumber = (_receiveSequenceNumber + 1) % int.MaxValue;
                } 
                while (remainedCount > 0 && currentWindowIndex < _maxDatagramWindowSize
                    && _windowState[currentWindowIndex] && _receiveSequenceNumber != 0);

                windowBufferReceiveAccessor.Array = _windowBuffer[windowIndex];
                windowBufferReceiveAccessor.Offset = 0;

                if (_receiveSequenceNumber == 0 || currentWindowIndex == _maxDatagramWindowSize)
                {
                    RestoreWindowState();

                    currentWindowIndex = 0;
                    windowStartSequenceNumber = _receiveSequenceNumber;
                    windowEndSequenceNumber = CalculateWindowEndSequenceNumber(windowStartSequenceNumber);
                }
            }

            await Console.Out.WriteLineAsync($"Need: {need}, Extra: {all - need}");

            return count;
        }

        private async Task WriteInternalAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            int datagramCount = count / _maxSendDatagramDataLength;
            int remainedBytes = count % _maxSendDatagramDataLength;

            if (remainedBytes != 0)
                datagramCount++;

            int windowStartSequenceNumber = 0, bufferIndex = offset, lastDatagramIndex = -1;
            int datagramSent = 0, datagramReceived = 0, dataReceivedTimeoutCount = 0;

            Task<UdpReceiveResult> resultTask = _dataReceivedResultTask;
            Task delayTask = null;
            Task<Task> whenAnyTask = null;

            if (resultTask == null)
            {
                resultTask = _udpClient.ReceiveAsync(cancellationToken).AsTask();
                _dataReceivedResultTask = resultTask;
            }

            while (datagramCount > 0)
            {
                if (datagramSent == datagramReceived)
                {
                    datagramSent = Math.Min(_maxDatagramWindowSize, int.MaxValue - _sendSequenceNumber);
                    datagramSent = Math.Min(datagramSent, datagramCount);

                    windowStartSequenceNumber = _sendSequenceNumber;

                    if (datagramCount == datagramSent)
                    {
                        lastDatagramIndex = datagramSent - 1;
                        _windowBufferSendAccessors[lastDatagramIndex] 
                            = new ReadOnlyMemory<byte>(_windowBuffer[lastDatagramIndex], 0, 
                            remainedBytes + DatagramLength.DataSentMinLength);
                    }

                    for (int i = 0; i < datagramSent; i++)
                    {
                        _windowState[i] = false;

                        byte[] sequenceNumberBytes = BitConverter.GetBytes(_sendSequenceNumber);
                        byte[] sendBuffer = _windowBuffer[i];

                        sendBuffer[0] = (byte)StatusCode.DataSent;
                        sendBuffer[1] = sequenceNumberBytes[0];
                        sendBuffer[2] = sequenceNumberBytes[1];
                        sendBuffer[3] = sequenceNumberBytes[2];
                        sendBuffer[4] = sequenceNumberBytes[3];

                        ReadOnlyMemory<byte> sendBufferAccessor = _windowBufferSendAccessors[i];
                        int copyCount = sendBufferAccessor.Length - DatagramLength.DataSentMinLength;

                        Buffer.BlockCopy(buffer, bufferIndex, sendBuffer, _sendHeaderLength, copyCount);
                        bufferIndex += copyCount;

                        await _udpClient.SendAsync(sendBufferAccessor, cancellationToken);

                        _sendSequenceNumber = (_sendSequenceNumber + 1) % int.MaxValue;
                    }

                    datagramReceived = 0;
                }
                else
                {
                    for (int i = 0; i < datagramSent; i++)
                    {
                        if (_windowState[i])
                            continue;

                        await _udpClient.SendAsync(_windowBufferSendAccessors[i], cancellationToken);
                    }
                }

                bool taskDelayed = false;

                if (whenAnyTask == null 
                    || (taskDelayed = whenAnyTask.IsCompletedSuccessfully && whenAnyTask.Result == delayTask))
                {
                    delayTask = Task.Delay(_dataReceivedWaitTime, cancellationToken);
                    whenAnyTask = Task.WhenAny(resultTask, delayTask);

                    if (taskDelayed)
                        dataReceivedTimeoutCount++;
                }

                while (await whenAnyTask == resultTask)
                {
                    UdpReceiveResult result = resultTask.Result;
                    byte[] resultBuffer = result.Buffer;

                    resultTask = _udpClient.ReceiveAsync(cancellationToken).AsTask();
                    delayTask = Task.Delay(_dataReceivedWaitTime, cancellationToken);
                    whenAnyTask = Task.WhenAny(resultTask, delayTask);

                    if (resultBuffer.Length < DatagramLength.DataReceivedLength
                        || (StatusCode)resultBuffer[0] != StatusCode.DataReceived)
                        continue;

                    dataReceivedTimeoutCount = 0;

                    int sequenceNumber = BitConverter.ToInt32(resultBuffer, 1);
                    int windowIndex = sequenceNumber - windowStartSequenceNumber;

                    if (windowIndex < 0 || sequenceNumber >= _sendSequenceNumber || _windowState[windowIndex])
                        continue;

                    //await Console.Out.WriteLineAsync($"Recv: {sequenceNumber}");

                    _windowState[windowIndex] = true;
                    datagramReceived++;
                    datagramCount--;
                }

                if (dataReceivedTimeoutCount == _maxDataReceivedTimeoutCount)
                    throw new TimeoutException("Failed to send data to the remote host in the allotted time.");
            }

            if (lastDatagramIndex >= 0)
            {
                _dataReceivedResultTask = resultTask;
                _windowBufferSendAccessors[lastDatagramIndex]
                            = new ReadOnlyMemory<byte>(_windowBuffer[lastDatagramIndex], 0, _maxSendDatagramLength);
            }
        }
    }
}
