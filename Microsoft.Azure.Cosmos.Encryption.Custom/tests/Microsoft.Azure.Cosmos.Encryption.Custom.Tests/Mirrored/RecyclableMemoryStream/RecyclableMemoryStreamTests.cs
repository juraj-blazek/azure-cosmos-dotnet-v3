// ---------------------------------------------------------------------
// Copyright (c) 2015 Microsoft
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
// ---------------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Encryption.Tests.Mirrored.RecyclableMemoryStream
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.IO;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Full test suite. It is abstract to allow parameters of the memory manager to be modified and tested in different
    /// combinations.
    /// </summary>
    public abstract class BaseRecyclableMemoryStreamTests
    {
        protected const int DefaultBlockSize = 16384;
        protected const int DefaultLargeBufferMultiple = 1 << 20;
        protected const int DefaultMaximumBufferSize = 8 * (1 << 20);
        protected const long DefaultVeryLargeStreamSize = 3L * (1L << 30);
        protected const long VeryLargeMaximumSize = 4L * (1L << 30);
        protected const string DefaultTag = "NUnit";
        protected static readonly Guid DefaultId = Guid.NewGuid();

        private readonly Random random = new();

        private readonly RecyclableMemoryStreamEventListener eventListener = new();

        [ClassCleanup]
        public void OneTimeTearDown()
        {
            // Make sure we saw ETW events for each event -- just a rough test to ensure they're being sent.
            for (int i = 1; i < this.eventListener.EventCounts.Length; i++)
            {
                Assert.IsTrue(this.eventListener.EventCounts[i] > 0, $"No events recorded for eventId {i}");
            }
            this.eventListener.Dispose();
        }

        #region RecyclableMemoryManager Tests
        [TestMethod]
        public void OptionsValueAndSettingsTheSame()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            memMgr.OptionsValue.BlockSize = 13;
            Assert.AreEqual(13, memMgr.Settings.BlockSize);
        }

        [TestMethod]
        public virtual void RecyclableMemoryManagerUsingMultipleOrExponentialLargeBuffer()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.IsFalse(memMgr.OptionsValue.UseExponentialLargeBuffer);
        }

        [TestMethod]
        public void RecyclableMemoryManagerSetsMaxPoolFreeBytes()
        {
            RecyclableMemoryStreamManager memMgr = new(new RecyclableMemoryStreamManager.Options(DefaultBlockSize, DefaultLargeBufferMultiple, DefaultMaximumBufferSize, DefaultBlockSize * 2, DefaultLargeBufferMultiple * 4));
            Assert.AreEqual(DefaultBlockSize * 2, memMgr.OptionsValue.MaximumSmallPoolFreeBytes);
            Assert.AreEqual(DefaultLargeBufferMultiple * 4, memMgr.OptionsValue.MaximumLargePoolFreeBytes);
            Assert.AreEqual(DefaultBlockSize, memMgr.OptionsValue.BlockSize);
        }

        [TestMethod]
        public void RecyclableMemoryManagerSetsBlockSizeLargeBufferMultipleAndMaximumBufferSize()
        {
            RecyclableMemoryStreamManager memMgr = new(new RecyclableMemoryStreamManager.Options { BlockSize = 10, LargeBufferMultiple = 1000, MaximumBufferSize = 8000 });
            Assert.AreEqual(10, memMgr.OptionsValue.BlockSize);
            Assert.AreEqual(1000, memMgr.OptionsValue.LargeBufferMultiple);
            Assert.AreEqual(8000, memMgr.OptionsValue.MaximumBufferSize);
        }

        [TestMethod]
        public void RecyclableMemoryManagerThrowsExceptionOnZeroBlockSize()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 0, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = -1, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 1, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer });
        }

        [TestMethod]
        public void RecyclableMemoryManagerThrowsExceptionOnZeroLargeBufferMultipleSize()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 0, MaximumBufferSize = 200, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = -1, MaximumBufferSize = 200, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer });
        }

        [TestMethod]
        public void RecyclableMemoryManagerThrowsExceptionOnMaximumBufferSizeLessThanBlockSize()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 100, MaximumBufferSize = 99, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 100, MaximumBufferSize = 100, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer });
        }

        [TestMethod]
        public virtual void RecyclableMemoryManagerThrowsExceptionOnMaximumBufferNotMultipleOrExponentialOfLargeBufferMultiple()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 2025, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 2023, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 2048, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer });
        }

        [TestMethod]
        public void RecyclableMemoryManagerThrowsExceptionOnNegativeMaxFreeSizes()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 1, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = false, MaximumSmallPoolFreeBytes = -1, MaximumLargePoolFreeBytes = 1000 }));
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 1, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = false, MaximumSmallPoolFreeBytes = 1000, MaximumLargePoolFreeBytes = -1 }));
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 1, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = false, MaximumSmallPoolFreeBytes = 1000, MaximumLargePoolFreeBytes = 1000 });
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 1, LargeBufferMultiple = 100, MaximumBufferSize = 200, UseExponentialLargeBuffer = false, MaximumSmallPoolFreeBytes = 0, MaximumLargePoolFreeBytes = 0 });
        }

        [TestMethod]
        public virtual void GetLargeBufferAlwaysAMultipleOrExponentialOfMegabyteAndAtLeastAsMuchAsRequestedForLargeBuffer()
        {
            const int step = 200000;
            const int start = 1;
            const int end = 16000000;
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();

            for (int i = start; i <= end; i += step)
            {
                byte[] buffer = memMgr.GetLargeBuffer(i, DefaultId, DefaultTag);
                Assert.IsTrue(buffer.Length >= i);
                Assert.AreEqual(0, buffer.Length % memMgr.OptionsValue.LargeBufferMultiple, $"buffer length of {buffer.Length} is not a multiple of {memMgr.OptionsValue.LargeBufferMultiple}");
            }
        }

        [TestMethod]
        public virtual void AllMultiplesOrExponentialUpToMaxCanBePooled()
        {
            const int BlockSize = 100;
            const int LargeBufferMultiple = 1000;
            const int MaxBufferSize = 8000;

            for (int size = LargeBufferMultiple; size <= MaxBufferSize; size += LargeBufferMultiple)
            {
                RecyclableMemoryStreamManager memMgr = new(
                    new RecyclableMemoryStreamManager.Options
                    {
                        BlockSize = BlockSize,
                        LargeBufferMultiple = LargeBufferMultiple,
                        MaximumBufferSize = MaxBufferSize,
                        UseExponentialLargeBuffer = this.UseExponentialLargeBuffer,
                        AggressiveBufferReturn = this.AggressiveBufferRelease
                    });
                byte[] buffer = memMgr.GetLargeBuffer(size, DefaultId, DefaultTag);
                Assert.AreEqual(0, memMgr.LargePoolFreeSize);
                Assert.AreEqual(size, memMgr.LargePoolInUseSize);

                memMgr.ReturnLargeBuffer(buffer, DefaultId, DefaultTag);

                Assert.AreEqual(size, memMgr.LargePoolFreeSize);
                Assert.AreEqual(0, memMgr.LargePoolInUseSize);
            }
        }

        /*
         * TODO: clocke to release logging libraries to enable some tests.
        [TestMethod]
        public void GetVeryLargeBufferRecordsCallStack()
        {
            var logger = LogManager.CreateMemoryLogger();
            logger.SubscribeToEvents(Events.Writer, EventLevel.Verbose);

            var memMgr = GetMemoryManager();
            memMgr.OptionsValue.GenerateCallStacks = true;
            var buffer = memMgr.GetLargeBuffer(memMgr.OptionsValue.MaximumBufferSize + 1, DefaultTag);
            // wait for log to flush
            GC.Collect(1);
            GC.WaitForPendingFinalizers();
            Thread.Sleep(250);

            var log = Encoding.UTF8.GetString(logger.Stream.GetBuffer(), 0, (int)logger.Stream.Length);
            Assert.That(log, Is.StringContaining("MemoryStreamNonPooledLargeBufferCreated"));
            Assert.That(log, Is.StringContaining("GetLargeBuffer"));
            Assert.That(log, Is.StringContaining(buffer.Length.ToString()));
        }
        */

        [TestMethod]
        public void ReturnLargerBufferWithNullBufferThrowsException()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.ThrowsException<ArgumentNullException>(() => memMgr.ReturnLargeBuffer(null!, DefaultId, DefaultTag));
        }

        [TestMethod]
        public void ReturnLargeBufferWithWrongSizedBufferThrowsException()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = new byte[100];
            Assert.ThrowsException<ArgumentException>(() => memMgr.ReturnLargeBuffer(buffer, DefaultId, DefaultTag));
        }

        [TestMethod]
        public void ReturnNullBlockThrowsException()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.ThrowsException<ArgumentNullException>(() => memMgr.ReturnBlock(null!, Guid.Empty, string.Empty));
        }

        [TestMethod]
        public void ReturnNullBlocksThrowsException()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.ThrowsException<ArgumentNullException>(() => memMgr.ReturnBlocks(null!, Guid.Empty, string.Empty));
        }

        [TestMethod]
        public void ReturnBlockWithInvalidBufferThrowsException()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = new byte[memMgr.OptionsValue.BlockSize + 1];
            Assert.ThrowsException<ArgumentException>(() => memMgr.ReturnBlock(buffer, Guid.Empty, string.Empty));
        }

        [TestMethod]
        public void ReturnBlocksWithInvalidBuffersThrowsException()
        {
            List<byte[]> buffers = new(3);
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            buffers.Add(memMgr.GetBlock());
            buffers.Add(new byte[memMgr.OptionsValue.BlockSize + 1]);
            buffers.Add(memMgr.GetBlock());
            Assert.ThrowsException<ArgumentException>(() => memMgr.ReturnBlocks(buffers, Guid.Empty, string.Empty));
        }

        [TestMethod]
        public void ReturnBlocksWithNullBufferThrowsException()
        {
            List<byte[]> buffers = new(3);
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            buffers.Add(memMgr.GetBlock());
            buffers.Add(null!);
            buffers.Add(memMgr.GetBlock());
            Assert.ThrowsException<ArgumentException>(() => memMgr.ReturnBlocks(buffers, Guid.Empty, string.Empty));
        }

        [TestMethod]
        public virtual void RequestTooLargeBufferAdjustsInUseCounter()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = memMgr.GetLargeBuffer(memMgr.OptionsValue.MaximumBufferSize + 1, DefaultId, DefaultTag);
            Assert.AreEqual(memMgr.OptionsValue.MaximumBufferSize + memMgr.OptionsValue.LargeBufferMultiple, buffer.Length);
            Assert.AreEqual(buffer.Length, memMgr.LargePoolInUseSize);
        }

        [TestMethod]
        public void ReturnTooLargeBufferDoesNotReturnToPool()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = memMgr.GetLargeBuffer(memMgr.OptionsValue.MaximumBufferSize + 1, DefaultId, DefaultTag);

            memMgr.ReturnLargeBuffer(buffer, DefaultId, DefaultTag);
            Assert.AreEqual(0, memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
        }

        [TestMethod]
        public void ReturnZeroLengthBufferThrowsException()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] emptyBuffer = Array.Empty<byte>();
            Assert.ThrowsException<ArgumentException>(() => memMgr.ReturnLargeBuffer(emptyBuffer, DefaultId, DefaultTag));
        }

        [TestMethod]
        public void ReturningBlockIsDroppedIfEnoughFree()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            const int MaxFreeBuffersAllowed = 2;
            const int BuffersToTest = MaxFreeBuffersAllowed + 1;

            // Only allow 2 blocks in the free pool at a time
            memMgr.OptionsValue.MaximumSmallPoolFreeBytes = MaxFreeBuffersAllowed * memMgr.OptionsValue.BlockSize;
            List<byte[]> buffers = new(BuffersToTest);
            for (int i = buffers.Capacity; i > 0; --i)
            {
                buffers.Add(memMgr.GetBlock());
            }

            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(BuffersToTest * memMgr.OptionsValue.BlockSize, memMgr.SmallPoolInUseSize);

            // All but one buffer should be returned to pool
            for (int i = 0; i < buffers.Count; i++)
            {
                memMgr.ReturnBlock(buffers[i], Guid.Empty, string.Empty);
            }
            Assert.AreEqual(memMgr.OptionsValue.MaximumSmallPoolFreeBytes, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }

        [TestMethod]
        public void ReturningBlocksAreDroppedIfEnoughFree()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            const int MaxFreeBuffersAllowed = 2;
            const int BuffersToTest = MaxFreeBuffersAllowed + 1;

            // Only allow 2 blocks in the free pool at a time
            memMgr.OptionsValue.MaximumSmallPoolFreeBytes = MaxFreeBuffersAllowed * memMgr.OptionsValue.BlockSize;
            List<byte[]> buffers = new(BuffersToTest);
            for (int i = buffers.Capacity; i > 0; --i)
            {
                buffers.Add(memMgr.GetBlock());
            }

            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(BuffersToTest * memMgr.OptionsValue.BlockSize, memMgr.SmallPoolInUseSize);

            // All but one buffer should be returned to pool
            memMgr.ReturnBlocks(buffers, Guid.Empty, string.Empty);
            Assert.AreEqual(memMgr.OptionsValue.MaximumSmallPoolFreeBytes, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }

        [TestMethod]
        public void ReturningBlocksNeverDroppedIfMaxFreeSizeZero()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();

            const int BuffersToTest = 99;

            memMgr.OptionsValue.MaximumSmallPoolFreeBytes = 0;
            List<byte[]> buffers = new(BuffersToTest);
            for (int i = buffers.Capacity; i > 0; --i)
            {
                buffers.Add(memMgr.GetBlock());
            }

            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(BuffersToTest * memMgr.OptionsValue.BlockSize, memMgr.SmallPoolInUseSize);

            memMgr.ReturnBlocks(buffers, Guid.Empty, string.Empty);
            Assert.AreEqual(BuffersToTest * memMgr.OptionsValue.BlockSize, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }

        [TestMethod]
        public void ReturningLargeBufferIsDroppedIfEnoughFree()
        {
            this.TestDroppingLargeBuffer(8000);
        }

        [TestMethod]
        public void ReturningLargeBufferNeverDroppedIfMaxFreeSizeZero()
        {
            this.TestDroppingLargeBuffer(0);
        }

        protected virtual void TestDroppingLargeBuffer(long maxFreeLargeBufferSize)
        {
            const int BlockSize = 100;
            const int LargeBufferMultiple = 1000;
            const int MaxBufferSize = 8000;

            for (int size = LargeBufferMultiple; size <= MaxBufferSize; size += LargeBufferMultiple)
            {
                RecyclableMemoryStreamManager memMgr = new(
                    new RecyclableMemoryStreamManager.Options
                    {
                        BlockSize = BlockSize,
                        LargeBufferMultiple = LargeBufferMultiple,
                        MaximumBufferSize = MaxBufferSize,
                        UseExponentialLargeBuffer = this.UseExponentialLargeBuffer,
                        AggressiveBufferReturn = this.AggressiveBufferRelease,
                        MaximumLargePoolFreeBytes = maxFreeLargeBufferSize
                    });

                List<byte[]> buffers = new();

                //Get one extra buffer
                long buffersToRetrieve = maxFreeLargeBufferSize > 0 ? (maxFreeLargeBufferSize / size) + 1 : 10;
                for (int i = 0; i < buffersToRetrieve; i++)
                {
                    buffers.Add(memMgr.GetLargeBuffer(size, DefaultId, DefaultTag));
                }
                Assert.AreEqual(size * buffersToRetrieve, memMgr.LargePoolInUseSize);
                Assert.AreEqual(0, memMgr.LargePoolFreeSize);
                foreach (byte[] buffer in buffers)
                {
                    memMgr.ReturnLargeBuffer(buffer, DefaultId, DefaultTag);
                }
                Assert.AreEqual(0, memMgr.LargePoolInUseSize);
                if (maxFreeLargeBufferSize > 0)
                {
                    Assert.IsTrue(memMgr.LargePoolFreeSize <= maxFreeLargeBufferSize);
                }
                else
                {
                    Assert.AreEqual(buffersToRetrieve * size, memMgr.LargePoolFreeSize);
                }
            }
        }

        [TestMethod]
        public void GettingBlockAdjustsFreeAndInUseSize()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);

            // This should create a new block
            byte[] block = memMgr.GetBlock();

            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, memMgr.SmallPoolInUseSize);

            memMgr.ReturnBlocks(new List<byte[]> { block }, Guid.Empty, string.Empty);

            Assert.AreEqual(memMgr.OptionsValue.BlockSize, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);

            // This should get an existing block
            block = memMgr.GetBlock();

            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, memMgr.SmallPoolInUseSize);

            memMgr.ReturnBlocks(new List<byte[]> { block }, Guid.Empty, string.Empty);

            Assert.AreEqual(memMgr.OptionsValue.BlockSize, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }
        #endregion

        #region GetBuffer/TryGetBuffer Tests
        [TestMethod]
        public void GetBufferReturnsSingleBlockForBlockSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize;
            byte[] buffer = this.GetRandomBuffer(size);
            stream.Write(buffer, 0, buffer.Length);
            byte[] returnedBuffer = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, returnedBuffer.Length);
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);
        }

        [TestMethod]
        public void GetBufferReturnsSingleBlockForLessThanBlockSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize - 1;
            byte[] buffer = this.GetRandomBuffer(size);
            stream.Write(buffer, 0, buffer.Length);
            byte[] returnedBuffer = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, returnedBuffer.Length);
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);
        }

        [TestMethod]
        public void GetBufferReturnsLargeBufferForMoreThanBlockSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize + 1;
            byte[] buffer = this.GetRandomBuffer(size);
            stream.Write(buffer, 0, buffer.Length);
            byte[] returnedBuffer = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, returnedBuffer.Length);
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);
        }

        [TestMethod]
        public void GetBufferReturnsSameLarge()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            byte[] returnedBuffer = stream.GetBuffer();
            byte[] returnedBuffer2 = stream.GetBuffer();
            RMSAssert.BuffersAreEqual(returnedBuffer, returnedBuffer2);
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);
        }

        [TestMethod]
        public void GetBufferAdjustsLargePoolFreeSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            int bufferLength = stream.MemoryManager.OptionsValue.BlockSize * 4;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, buffer.Length);

            byte[] newBuffer = stream.GetBuffer();
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);

            stream.Dispose();

            Assert.AreEqual(newBuffer.Length, memMgr.LargePoolFreeSize);

            RecyclableMemoryStream newStream = new(memMgr);
            newStream.Write(buffer, 0, buffer.Length);

            byte[] newBuffer2 = newStream.GetBuffer();
            Assert.AreEqual(newBuffer.Length, newBuffer2.Length);
            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            RMSAssert.TryGetBufferEqualToGetBuffer(newStream);
        }

        [TestMethod]
        public void TryGetBufferFailsOnLargeStream()
        {
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            // Exception path -- no content, but GetBuffer will throw
            Assert.IsFalse(stream.TryGetBuffer(out ArraySegment<byte> seg));
            Assert.AreEqual(0, seg.Offset);
            Assert.AreEqual(0, seg.Count);
            Assert.AreEqual(0, seg.Array.Length);

            //Non-exception path. Length is too long. No exception.
            byte[] buffer = new byte[RecyclableMemoryStreamManager.MaxArrayLength];
            stream.Write(buffer);
            stream.Write(buffer);
            Assert.IsFalse(stream.TryGetBuffer(out _));
        }

        [TestMethod]
        public void CallingWriteAfterLargeGetBufferDoesNotLoseData()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Capacity = stream.MemoryManager.OptionsValue.BlockSize + 1;
            byte[] buffer = stream.GetBuffer();
            buffer[stream.MemoryManager.OptionsValue.BlockSize] = 13;

            stream.Position = stream.MemoryManager.OptionsValue.BlockSize + 1;
            byte[] bytesToWrite = this.GetRandomBuffer(10);
            stream.Write(bytesToWrite, 0, bytesToWrite.Length);

            buffer = stream.GetBuffer();

            Assert.AreEqual(13, buffer[stream.MemoryManager.OptionsValue.BlockSize]);
            RMSAssert.BuffersAreEqual(buffer, stream.MemoryManager.OptionsValue.BlockSize + 1, bytesToWrite, 0, bytesToWrite.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize + 1 + bytesToWrite.Length, stream.Position);
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);
        }

        [TestMethod]
        public void CallingWriteByteAfterLargeGetBufferDoesNotLoseData()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Capacity = stream.MemoryManager.OptionsValue.BlockSize + 1;
            byte[] buffer = stream.GetBuffer();
            buffer[stream.MemoryManager.OptionsValue.BlockSize] = 13;

            stream.Position = stream.MemoryManager.OptionsValue.BlockSize + 1;
            stream.WriteByte(14);

            buffer = stream.GetBuffer();

            Assert.AreEqual(13, buffer[stream.MemoryManager.OptionsValue.BlockSize]);
            Assert.AreEqual(14, buffer[stream.MemoryManager.OptionsValue.BlockSize + 1]);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize + 2, stream.Position);
            RMSAssert.TryGetBufferEqualToGetBuffer(stream);
        }

        [TestMethod]
        public void MaxIntAllocationSucceeds()
        {
            RecyclableMemoryStreamManager mgr = new();

            for (int i = -1; i < 2; ++i)
            {
                int requestedSize = int.MaxValue - (mgr.OptionsValue.BlockSize + i);
                RecyclableMemoryStream stream = mgr.GetStream(null, requestedSize);
                Assert.IsTrue(stream.Capacity64 >= requestedSize);
            }

            RecyclableMemoryStream maxStream = mgr.GetStream(null, int.MaxValue);
            Assert.IsTrue(maxStream.Capacity64 >= int.MaxValue);
        }

        #endregion

        #region GetSpan/Memory Tests
        [TestMethod]
        public void GetSpanMemoryWithNegativeHintFails()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.GetSpan(-1));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.GetMemory(-1));
        }

        [TestMethod]
        public void GetSpanMemoryWithTooLargeHintFails()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Position = 1;
            Assert.ThrowsException<OutOfMemoryException>(() => stream.GetSpan(int.MaxValue));
            Assert.ThrowsException<OutOfMemoryException>(() => stream.GetMemory(int.MaxValue));
        }

        [TestMethod]
        public void GetSpanMemoryWithHintLargerThanMaximumStreamCapacityFails()
        {
            RecyclableMemoryStreamManager memoryManager = this.GetMemoryManager();
            memoryManager.OptionsValue.MaximumStreamCapacity = short.MaxValue;
            RecyclableMemoryStream stream = new(memoryManager, string.Empty, 0);
            Assert.ThrowsException<OutOfMemoryException>(() => stream.GetSpan(short.MaxValue + 1));
            Assert.ThrowsException<OutOfMemoryException>(() => stream.GetMemory(short.MaxValue + 1));
        }

        [TestMethod]
        public void GetSpanMemoryReturnsSingleBlockWithNoHint()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = stream.GetBuffer();
            Span<byte> span = stream.GetSpan();
            Memory<byte> memory = stream.GetMemory();
            MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> arraySegment);
            Assert.AreEqual(buffer, arraySegment.Array);
            Assert.IsTrue(buffer.AsSpan() == span);
        }

        [TestMethod]
        public void GetSpanMemoryReturnsLargeBufferWithNoHint()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Capacity = stream.MemoryManager.OptionsValue.BlockSize + 1;
            byte[] buffer = stream.GetBuffer();
            Span<byte> span = stream.GetSpan();
            Memory<byte> memory = stream.GetMemory();
            MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> arraySegment);
            Assert.AreEqual(buffer, arraySegment.Array);
            Assert.IsTrue(buffer.AsSpan() == span);
            stream.Advance(1);
            Assert.AreEqual(1, stream.Length);
        }

        [TestMethod]
        public void GetSpanMemoryReturnsSingleBlockWithNoHintPositionedMidBlock()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Advance(1);
            byte[] buffer = stream.GetBuffer();
            Span<byte> span = stream.GetSpan();
            Memory<byte> memory = stream.GetMemory();
            MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> arraySegment);
            Assert.AreEqual(buffer, arraySegment.Array);
            Assert.AreEqual(buffer.Length - 1, memory.Length);
            Assert.IsTrue(buffer.AsSpan(1) == span);
        }

        [TestMethod]
        public void GetSpanMemoryReturnsNewBlockWithNoHintPositionedEndOfBlock()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize;

            stream.Position = size;
            Span<byte> span = stream.GetSpan();
            Memory<byte> memory = stream.GetMemory();
            this.GetRandomBuffer(size).AsSpan().CopyTo(span);
            stream.Advance(size);
            MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> arraySegment);
            byte[] memoryArray = arraySegment.Array;

            Assert.IsTrue(span == memory.Span);
            Assert.AreEqual(size, span.Length);
            Assert.AreEqual(size, memoryArray.Length);
            Span<byte> bufferSpan = stream.GetBuffer().AsSpan(size, size);
            Assert.IsTrue(bufferSpan.SequenceEqual(span));
            Assert.IsFalse(bufferSpan == span);
        }

        [TestMethod]
        public void GetSpanMemoryReturnsLargeTempBufferWhenHintIsLongerThanBlock()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize + 1;

            Span<byte> span = stream.GetSpan(size + 1);
            byte[] randomBuffer = this.GetRandomBuffer(size);
            randomBuffer.AsSpan().CopyTo(span);
            stream.Advance(size);

            Span<byte> bufferSpan = stream.GetBuffer().AsSpan(0, size);
            Assert.IsTrue(bufferSpan.SequenceEqual(randomBuffer));
            Assert.IsFalse(bufferSpan == span);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, span.Length);
        }

        [TestMethod]
        public void GetSpanMemoryReturnsBlockTempBufferWhenHintGoesPastEndOfBlock()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize / 2;

            stream.Position = size + 1;
            Span<byte> span = stream.GetSpan(size);
            byte[] randomBuffer = this.GetRandomBuffer(size);
            randomBuffer.AsSpan().CopyTo(span);
            stream.Advance(size);

            Span<byte> bufferSpan = stream.GetBuffer().AsSpan(size + 1, size);
            Assert.IsTrue(bufferSpan.SequenceEqual(randomBuffer));
            Assert.IsFalse(bufferSpan == span);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, span.Length);
        }
        #endregion

        #region GetReadOnlySequence Tests
        [TestMethod]
        public void GetReadOnlySequenceReturnsSingleBlockForBlockSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize;
            byte[] buffer = this.GetRandomBuffer(size);
            stream.Write(buffer, 0, buffer.Length);
            ReadOnlySequence<byte> returnedSequence = stream.GetReadOnlySequence();
            Assert.IsTrue(returnedSequence.IsSingleSegment);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, returnedSequence.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, returnedSequence.First.Length);
            Assert.IsTrue(MemoryMarshal.TryGetArray(returnedSequence.First, out ArraySegment<byte> arraySegment));
            RMSAssert.BuffersAreEqual(arraySegment.Array, stream.GetBuffer(), stream.GetBuffer().Length);
        }

        [TestMethod]
        public void GetReadOnlySequenceReturnsSingleBlockForLessThanBlockSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize - 1;
            byte[] buffer = this.GetRandomBuffer(size);
            stream.Write(buffer, 0, buffer.Length);
            ReadOnlySequence<byte> returnedSequence = stream.GetReadOnlySequence();
            Assert.IsTrue(returnedSequence.IsSingleSegment);
            Assert.AreEqual(size, returnedSequence.Length);
            Assert.AreEqual(size, returnedSequence.First.Length);
            Assert.IsTrue(MemoryMarshal.TryGetArray(returnedSequence.First, out ArraySegment<byte> arraySegment));
            RMSAssert.BuffersAreEqual(arraySegment.Array, stream.GetBuffer(), stream.GetBuffer().Length);
        }

        [TestMethod]
        public void GetReadOnlySequenceReturnsMultipleBuffersForMoreThanBlockSize()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int size = stream.MemoryManager.OptionsValue.BlockSize + 1;
            byte[] buffer = this.GetRandomBuffer(size);
            stream.Write(buffer, 0, buffer.Length);
            ReadOnlySequence<byte> returnedSequence = stream.GetReadOnlySequence();
            stream.TryGetBuffer(out ArraySegment<byte> getBufferArraySegment);
            Assert.IsFalse(returnedSequence.IsSingleSegment);
            Assert.AreEqual(size, returnedSequence.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, returnedSequence.First.Length);
            Assert.IsTrue(returnedSequence.Slice(stream.MemoryManager.OptionsValue.BlockSize).IsSingleSegment);
            Assert.AreEqual(1, returnedSequence.Slice(stream.MemoryManager.OptionsValue.BlockSize).Length);
            RMSAssert.BuffersAreEqual(returnedSequence.ToArray(), getBufferArraySegment, (int)returnedSequence.Length);
        }

        [TestMethod]
        public void GetReadOnlySequenceReturnsLargeAfterGetBuffer()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            stream.TryGetBuffer(out ArraySegment<byte> getBufferArraySegment);
            ReadOnlySequence<byte> returnedSequence = stream.GetReadOnlySequence();
            Assert.IsTrue(returnedSequence.IsSingleSegment);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, returnedSequence.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, returnedSequence.First.Length);
            Assert.IsTrue(MemoryMarshal.TryGetArray(returnedSequence.First, out _));
            RMSAssert.BuffersAreEqual(returnedSequence.ToArray(), getBufferArraySegment, (int)returnedSequence.Length);
        }

        [TestMethod]
        public void GetReadOnlySequenceReturnsSameLarge()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            stream.TryGetBuffer(out ArraySegment<byte> arraySegment0);
            ReadOnlySequence<byte> returnedSequence1 = stream.GetReadOnlySequence();
            ReadOnlySequence<byte> returnedSequence2 = stream.GetReadOnlySequence();
            Assert.IsTrue(MemoryMarshal.TryGetArray(returnedSequence1.First, out ArraySegment<byte> arraySegment1));
            Assert.IsTrue(MemoryMarshal.TryGetArray(returnedSequence2.First, out ArraySegment<byte> arraySegment2));
            RMSAssert.BuffersAreEqual(arraySegment0, arraySegment2);
            RMSAssert.BuffersAreEqual(arraySegment1, arraySegment2);
        }

        [TestMethod]
        public void GetReadOnlySequenceReturnsSequenceWithSameLengthAsStreamAfterStreamShrinks()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Position = DefaultBlockSize;
            stream.WriteByte(0);
            stream.SetLength(DefaultBlockSize - 1);
            Assert.AreEqual(stream.Length, stream.GetReadOnlySequence().Length);
        }
        #endregion

        #region Constructor tests
        [TestMethod]
        public void StreamHasGuid()
        {
            Guid expectedGuid = Guid.NewGuid();
            RecyclableMemoryStream stream1 = new(this.GetMemoryManager(), expectedGuid);
            RecyclableMemoryStream stream2 = this.GetMemoryManager().GetStream(expectedGuid);
            Assert.AreEqual(expectedGuid, stream1.Id);
            Assert.AreEqual(expectedGuid, stream2.Id);
        }

        [TestMethod]
        public void StreamHasTag()
        {
            const string expectedTag = "Unit test";

            RecyclableMemoryStream stream1 = new(this.GetMemoryManager(), expectedTag);
            RecyclableMemoryStream stream2 = this.GetMemoryManager().GetStream(expectedTag);
            Assert.AreNotEqual(Guid.Empty, stream1.Id);
            Assert.AreEqual(expectedTag, stream1.Tag);
            Assert.AreNotEqual(Guid.Empty, stream2.Id);
            Assert.AreEqual(expectedTag, stream2.Tag);
        }

        [TestMethod]
        public void StreamHasGuidAndTag()
        {
            const string expectedTag = "Unit test";
            Guid expectedGuid = Guid.NewGuid();
            RecyclableMemoryStream stream1 = new(this.GetMemoryManager(), expectedGuid, expectedTag);
            RecyclableMemoryStream stream2 = this.GetMemoryManager().GetStream(expectedGuid, expectedTag);
            Assert.AreEqual(expectedGuid, stream1.Id);
            Assert.AreEqual(expectedTag, stream1.Tag);
            Assert.AreEqual(expectedGuid, stream2.Id);
            Assert.AreEqual(expectedTag, stream2.Tag);
        }

        [TestMethod]
        public void StreamHasDefaultCapacity()
        {
            RecyclableMemoryStreamManager memoryManager = this.GetMemoryManager();
            RecyclableMemoryStream stream = new(memoryManager);
            Assert.AreEqual(memoryManager.OptionsValue.BlockSize, stream.Capacity);
        }

        [TestMethod]
        public void ActualCapacityAtLeastRequestedCapacityAndMultipleOfBlockSize()
        {
            RecyclableMemoryStreamManager memoryManager = this.GetMemoryManager();
            int requestedSize = memoryManager.OptionsValue.BlockSize + 1;
            RecyclableMemoryStream stream = new(memoryManager, string.Empty, requestedSize);
            Assert.IsTrue(stream.Capacity >= requestedSize);
            Assert.AreEqual(0, stream.Capacity % memoryManager.OptionsValue.BlockSize, "stream capacity is not a multiple of the block size");
        }

        [TestMethod]
        public void IdTagAndRequestedSizeSet()
        {
            RecyclableMemoryStreamManager memoryManager = this.GetMemoryManager();
            int requestedSize = memoryManager.OptionsValue.BlockSize + 1;
            Guid expectedGuid = Guid.NewGuid();
            RecyclableMemoryStream stream = new(memoryManager, expectedGuid, "Tag", requestedSize);
            Assert.AreEqual(expectedGuid, stream.Id);
            Assert.AreEqual("Tag", stream.Tag);
            Assert.IsTrue(stream.Capacity >= requestedSize);

        }

        [TestMethod]
        public void AllocationStackIsRecorded()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            memMgr.OptionsValue.GenerateCallStacks = true;

            RecyclableMemoryStream stream = new(memMgr);

            StringAssert.Contains(stream.AllocationStack, "RecyclableMemoryStream..ctor");
            stream.Dispose();

            memMgr.OptionsValue.GenerateCallStacks = false;

            RecyclableMemoryStream stream2 = new(memMgr);

            Assert.IsNull(stream2.AllocationStack);

            stream2.Dispose();
        }
        #endregion

        #region Write Tests
        [TestMethod]
        public void WriteUpdatesLengthAndPosition()
        {
            const int expectedLength = 100;
            RecyclableMemoryStreamManager memoryManager = this.GetMemoryManager();
            RecyclableMemoryStream stream = new(memoryManager, string.Empty, expectedLength);
            byte[] buffer = this.GetRandomBuffer(expectedLength);
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(expectedLength, stream.Length);
            Assert.AreEqual(expectedLength, stream.Position);
        }

        [TestMethod]
        public void WriteInMiddleOfBufferDoesNotChangeLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int expectedLength = 100;
            byte[] buffer = this.GetRandomBuffer(expectedLength);
            stream.Write(buffer, 0, expectedLength);
            Assert.AreEqual(expectedLength, stream.Length);
            int smallBufferLength = 25;
            byte[] smallBuffer = this.GetRandomBuffer(smallBufferLength);
            stream.Position = 0;
            stream.Write(smallBuffer, 0, smallBufferLength);
            Assert.AreEqual(expectedLength, stream.Length);
        }

        [TestMethod]
        public void WriteSmallBufferStoresDataCorrectly()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, buffer.Length);
            RMSAssert.BuffersAreEqual(buffer, stream.GetBuffer(), buffer.Length);
        }

        [TestMethod]
        public void WriteLargeBufferStoresDataCorrectly()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize + 1);
            stream.Write(buffer, 0, buffer.Length);
            RMSAssert.BuffersAreEqual(buffer, stream.GetBuffer(), buffer.Length);
        }

        [TestMethod]
        public void WritePastEndIncreasesCapacity()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize);
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(DefaultBlockSize, stream.Capacity);
            Assert.AreEqual(DefaultBlockSize, stream.MemoryManager.SmallPoolInUseSize);
            stream.Write(new byte[1], 0, 1);
            Assert.AreEqual(2 * DefaultBlockSize, stream.Capacity);
            Assert.AreEqual(2 * DefaultBlockSize, stream.MemoryManager.SmallPoolInUseSize);
        }

        [TestMethod]
        public void WritePastEndOfLargeBufferIncreasesCapacityAndCopiesBuffer()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            byte[] get1 = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, get1.Length);
            stream.Write(buffer, 0, 1);
            byte[] get2 = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple + 1, stream.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple * 2, get2.Length);
            RMSAssert.BuffersAreEqual(get1, get2, (int)stream.Length - 1);
            Assert.AreEqual(buffer[0], get2[stream.MemoryManager.OptionsValue.LargeBufferMultiple]);
        }

        [TestMethod]
        public void WriteAfterLargeBufferDoesNotAllocateMoreBlocks()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize + 1);
            stream.Write(buffer, 0, buffer.Length);
            long inUseBlockBytes = stream.MemoryManager.SmallPoolInUseSize;
            stream.GetBuffer();
            Assert.IsTrue(stream.MemoryManager.SmallPoolInUseSize <= inUseBlockBytes);
            stream.Write(buffer, 0, buffer.Length);
            Assert.IsTrue(stream.MemoryManager.SmallPoolInUseSize <= inUseBlockBytes);
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            stream.Dispose();
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }

        [TestMethod]
        public void WriteNullBufferThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentNullException>(() => stream.Write(null!, 0, 0));
        }

        [TestMethod]
        public void WriteStartPastBufferThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream.Write(new byte[2], 2, 1));
        }

        [TestMethod]
        public void WriteStartBeforeBufferThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.Write(new byte[2], -1, 0));
        }

        [TestMethod]
        public void WriteNegativeCountThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.Write(new byte[2], 0, -1));
        }

        [TestMethod]
        public void WriteCountOutOfRangeThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream.Write(new byte[2], 0, 3));
        }

        // This is a valid test, but it's too resource-intensive to run on a regular basis.
        //[TestMethod]
        //public void WriteOverflowThrowsException()
        //{
        //    var stream = GetDefaultStream();
        //    int divisor = 256;
        //    var buffer = GetRandomBuffer(Int32.MaxValue / divisor);
        //    Assert.Throws<IOException>(() =>
        //    {
        //        for (int i = 0; i < divisor + 1; i++)
        //        {
        //            stream.Write(buffer, 0, buffer.Length);
        //        }
        //    });
        //}

        [TestMethod]
        public void WriteUpdatesPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = (stream.MemoryManager.OptionsValue.BlockSize / 2) + 1;
            byte[] buffer = this.GetRandomBuffer(bufferLength);

            for (int i = 0; i < 10; ++i)
            {
                stream.Write(buffer, 0, bufferLength);
                Assert.AreEqual((i + 1) * bufferLength, stream.Position);
            }
        }

        [TestMethod]
        public void WriteAfterEndIncreasesLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int initialPosition = 13;
            stream.Position = initialPosition;

            byte[] buffer = this.GetRandomBuffer(10);
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(stream.Position, stream.Length);
            Assert.AreEqual(initialPosition + buffer.Length, stream.Length);
        }

        #endregion

        #region Write Span Tests
        [TestMethod]
        public void WriteSpanUpdatesLengthAndPosition()
        {
            const int expectedLength = 100;
            RecyclableMemoryStreamManager memoryManager = this.GetMemoryManager();
            RecyclableMemoryStream stream = new(memoryManager, string.Empty, expectedLength);
            byte[] buffer = this.GetRandomBuffer(expectedLength);
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(expectedLength, stream.Length);
            Assert.AreEqual(expectedLength, stream.Position);
        }

        [TestMethod]
        public void WriteSpanInMiddleOfBufferDoesNotChangeLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int expectedLength = 100;
            byte[] buffer = this.GetRandomBuffer(expectedLength);
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(expectedLength, stream.Length);
            int smallBufferLength = 25;
            byte[] smallBuffer = this.GetRandomBuffer(smallBufferLength);
            stream.Position = 0;
            stream.Write(smallBuffer.AsSpan());
            Assert.AreEqual(expectedLength, stream.Length);
        }

        [TestMethod]
        public void WriteSpanSmallBufferStoresDataCorrectly()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer.AsSpan());
            RMSAssert.BuffersAreEqual(buffer, stream.GetBuffer(), buffer.Length);
        }

        [TestMethod]
        public void WriteSpanLargeBufferStoresDataCorrectly()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize + 1);
            stream.Write(buffer.AsSpan());
            RMSAssert.BuffersAreEqual(buffer, stream.GetBuffer(), buffer.Length);
        }

        [TestMethod]
        public void WriteSpanPastEndIncreasesCapacity()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize);
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(DefaultBlockSize, stream.Capacity);
            Assert.AreEqual(DefaultBlockSize, stream.MemoryManager.SmallPoolInUseSize);
            stream.Write(new byte[1]);
            Assert.AreEqual(2 * DefaultBlockSize, stream.Capacity);
            Assert.AreEqual(2 * DefaultBlockSize, stream.MemoryManager.SmallPoolInUseSize);
        }

        [TestMethod]
        public void WriteSpanPastEndOfLargeBufferIncreasesCapacityAndCopiesBuffer()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer.AsSpan());
            byte[] get1 = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, get1.Length);
            stream.Write(buffer.AsSpan(0, 1));
            byte[] get2 = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple + 1, stream.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple * 2, get2.Length);
            RMSAssert.BuffersAreEqual(get1, get2, (int)stream.Length - 1);
            Assert.AreEqual(buffer[0], get2[stream.MemoryManager.OptionsValue.LargeBufferMultiple]);
        }

        [TestMethod]
        public void WriteSpanAfterLargeBufferDoesNotAllocateMoreBlocks()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize + 1);
            stream.Write(buffer.AsSpan());
            long inUseBlockBytes = stream.MemoryManager.SmallPoolInUseSize;
            stream.GetBuffer();
            Assert.IsTrue(stream.MemoryManager.SmallPoolInUseSize <= inUseBlockBytes);
            stream.Write(buffer.AsSpan());
            Assert.IsTrue(stream.MemoryManager.SmallPoolInUseSize <= inUseBlockBytes);
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            stream.Dispose();
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }

        [TestMethod]
        public void WriteSpanUpdatesPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = (stream.MemoryManager.OptionsValue.BlockSize / 2) + 1;
            byte[] buffer = this.GetRandomBuffer(bufferLength);

            for (int i = 0; i < 10; ++i)
            {
                stream.Write(buffer.AsSpan());
                Assert.AreEqual((i + 1) * bufferLength, stream.Position);
            }
        }

        [TestMethod]
        public void WriteSpanAfterEndIncreasesLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int initialPosition = 13;
            stream.Position = initialPosition;

            byte[] buffer = this.GetRandomBuffer(10);
            stream.Write(buffer.AsSpan());
            Assert.AreEqual(stream.Position, stream.Length);
            Assert.AreEqual(initialPosition + buffer.Length, stream.Length);
        }

        #endregion

        #region WriteByte tests
        [TestMethod]
        public void WriteByteInMiddleSetsCorrectValue()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, bufferLength);
            stream.Position = 0;

            byte[] buffer2 = this.GetRandomBuffer(100);

            for (int i = 0; i < bufferLength; ++i)
            {
                stream.WriteByte(buffer2[i]);
            }

            byte[] newBuffer = stream.GetBuffer();
            for (int i = 0; i < bufferLength; ++i)
            {
                Assert.AreEqual(buffer2[i], newBuffer[i]);
            }
        }

        [TestMethod]
        public void WriteByteAtEndSetsCorrectValue()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.Capacity);
            stream.Write(buffer, 0, buffer.Length);

            const int testValue = 255;
            stream.WriteByte(testValue);
            stream.WriteByte(testValue);
            byte[] newBuffer = stream.GetBuffer();
            Assert.AreEqual(testValue, newBuffer[buffer.Length]);
            Assert.AreEqual(testValue, newBuffer[buffer.Length + 1]);
        }

        [TestMethod]
        public void WriteByteAtEndIncreasesLengthByOne()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.WriteByte(255);
            Assert.AreEqual(1, stream.Length);

            stream.Position = 0;

            byte[] buffer = this.GetRandomBuffer(stream.Capacity);
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(buffer.Length, stream.Length);
            stream.WriteByte(255);
            Assert.AreEqual(buffer.Length + 1, stream.Length);
        }

        [TestMethod]
        public void WriteByteInMiddleDoesNotChangeLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            Assert.AreEqual(bufferLength, stream.Length);
            stream.Position = bufferLength / 2;
            stream.WriteByte(255);
            Assert.AreEqual(bufferLength, stream.Length);
        }

        [TestMethod]
        public void WriteByteDoesNotIncreaseCapacity()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.Capacity;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            Assert.AreEqual(bufferLength, stream.Capacity);

            stream.Position = bufferLength / 2;
            stream.WriteByte(255);
            Assert.AreEqual(bufferLength, stream.Capacity);
        }

        [TestMethod]
        public void WriteByteIncreasesCapacity()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.Capacity;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            stream.WriteByte(255);
            Assert.AreEqual(2 * bufferLength, stream.Capacity);
        }

        [TestMethod]
        public void WriteByteUpdatesPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int end = stream.Capacity + 1;
            for (int i = 0; i < end; i++)
            {
                stream.WriteByte(255);
                Assert.AreEqual(i + 1, stream.Position);
            }
        }

        [TestMethod]
        public void WriteByteUpdatesLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Position = 13;
            stream.WriteByte(255);
            Assert.AreEqual(14, stream.Length);
        }

        [TestMethod]
        public void WriteByteAtEndOfLargeBufferIncreasesCapacity()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Capacity = stream.MemoryManager.OptionsValue.BlockSize * 2;
            int bufferLength = stream.Capacity;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, stream.Capacity);
            stream.Position = stream.MemoryManager.OptionsValue.LargeBufferMultiple;
            stream.WriteByte(255);
            Assert.IsTrue(stream.Capacity > stream.MemoryManager.OptionsValue.LargeBufferMultiple);
        }
        #endregion

        #region SafeReadByte Tests
        [TestMethod]
        public void SafeReadByteDoesNotUpdateStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetRandomStream();
            for (long i = 0L; i < stream.Length; i++)
            {
                long position = i;
                stream.SafeReadByte(ref position);
                Assert.AreEqual(i + 1, position);
                Assert.AreEqual(0, stream.Position);
            }
        }

        [TestMethod]
        public void SafeReadByteDoesNotDependOnStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.Capacity * 2);
            stream.Write(buffer, 0, buffer.Length);

            for (long i = 0L; i < stream.Length; i++)
            {
                stream.Position = this.random.Next(0, buffer.Length - 1);
                long position = i;
                int read = stream.SafeReadByte(ref position);
                Assert.AreEqual(buffer[i], read);
                Assert.AreEqual(i + 1, position);
            }
        }

        [TestMethod]
        public void SafeReadByteCanBeUsedInParallel()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            void read()
            {
                for (int i = 0; i < 1000; i++)
                {
                    long position = this.random.Next(0, bufferLength);
                    int byteRead = stream.SafeReadByte(ref position);

                    Assert.AreEqual(buffer[position - 1], byteRead);
                }
            }

            Parallel.For(0, 100, i => read());
        }

        [TestMethod]
        public void SafeReadByte_BlocksAndLargeBufferSame()
        {
            byte[] buffer = this.GetRandomBuffer(this.GetMemoryManager().OptionsValue.BlockSize * 2);
            RecyclableMemoryStream stream1 = this.GetDefaultStream();
            RecyclableMemoryStream stream2 = this.GetDefaultStream();
            stream1.Write(buffer);
            stream2.Write(buffer);
            stream2.GetBuffer();
            Assert.AreEqual(stream1.MemoryManager.OptionsValue.BlockSize * 2, stream1.Capacity);
            Assert.AreEqual(stream2.MemoryManager.OptionsValue.LargeBufferMultiple, stream2.Capacity);

            for (long i = 0L; i < stream1.Length; i++)
            {
                long position = i;
                int a = stream1.SafeReadByte(ref position);
                position = i;
                int b = stream2.SafeReadByte(ref position);
                Assert.AreEqual(a, b);

            }
        }
        #endregion

        #region ReadByte Tests
        [TestMethod]
        public void ReadByteUpdatesPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.Capacity * 2);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = 0;
            for (int i = 0; i < stream.Length; i++)
            {
                stream.ReadByte();
                Assert.AreEqual(i + 1, stream.Position);
            }
        }

        [TestMethod]
        public void ReadByteAtEndReturnsNegOne()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(bufferLength, stream.Position);
            Assert.AreEqual(-1, stream.ReadByte());
            Assert.AreEqual(bufferLength, stream.Position);
        }

        [TestMethod]
        public void ReadByteReturnsCorrectValueFromBlocks()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = 0;
            for (int i = 0; i < stream.Length; i++)
            {
                int b = stream.ReadByte();
                Assert.AreEqual(buffer[i], b);
            }
        }

        [TestMethod]
        public void ReadByteReturnsCorrectValueFromLargeBuffer()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = 0;
            byte[] copy = new byte[buffer.Length];
            for (int i = 0; i < stream.Length; i++)
            {
                copy[i] = (byte)stream.ReadByte();
            }
            RMSAssert.BuffersAreEqual(buffer, copy);
            RMSAssert.BuffersAreEqual(buffer, stream.GetBuffer(), buffer.Length);
        }
        #endregion

        #region SafeRead Tests
        [TestMethod]
        public void SafeReadDoesNotUpdateStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetRandomStream();

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            int bytesRead = 0;
            long position = 0L;

            while (position < stream.Length)
            {
                bytesRead += stream.SafeRead(destinationBuffer, 0, Math.Min(step, (int)stream.Length - bytesRead), ref position);
                Assert.AreEqual(bytesRead, position);
                Assert.AreEqual(0, stream.Position);
            }
        }

        [TestMethod]
        public void SafeReadDoesNotDependOnStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            byte[] expected = new byte[step];
            int bytesRead = 0;
            long position = 0L;

            while (position < stream.Length)
            {
                stream.Position = this.random.Next(0, bufferLength);
                long lastPosition = position;
                int lastRead = stream.SafeRead(destinationBuffer, 0, Math.Min(step, (int)stream.Length - bytesRead), ref position);
                bytesRead += lastRead;

                Array.Copy(buffer, lastPosition, expected, 0, lastRead);

                Assert.AreEqual(bytesRead, position);
                RMSAssert.BuffersAreEqual(destinationBuffer, expected, lastRead);
            }
        }

        [TestMethod]
        public void SafeReadCallsDoNotAffectOtherSafeReadCalls()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            int stepSlow = stream.MemoryManager.OptionsValue.BlockSize / 4;
            int stepFast = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] readBuffer = new byte[stepFast];
            MemoryStream readSlow = new();
            MemoryStream readFast = new();

            long positionSlow = 0L;
            long positionFast = 0L;

            while (positionFast < stream.Length)
            {
                int read = stream.SafeRead(readBuffer, 0, stepFast, ref positionFast);
                readFast.Write(readBuffer, 0, read);
                read = stream.SafeRead(readBuffer, 0, stepSlow, ref positionSlow);
                readSlow.Write(readBuffer, 0, read);
            }
            while (positionSlow < stream.Length)
            {
                int read = stream.SafeRead(readBuffer, 0, stepSlow, ref positionSlow);
                readSlow.Write(readBuffer, 0, read);
            }

            RMSAssert.BuffersAreEqual(readSlow.ToArray(), buffer);
            RMSAssert.BuffersAreEqual(readFast.ToArray(), buffer);
        }

        [TestMethod]
        public void SafeReadCanBeUsedInParallel()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            void read()
            {
                for (int i = 0; i < 5; i++)
                {
                    long position = this.random.Next(0, bufferLength);
                    long startPosition = position;
                    int length = this.random.Next(0, (int)(bufferLength - position));
                    byte[] readBuffer = new byte[length];
                    int bytesRead = stream.SafeRead(readBuffer, 0, length, ref position);

                    RMSAssert.BuffersAreEqual(readBuffer, 0, buffer, (int)startPosition, bytesRead);
                }
            }

            Parallel.For(0, 5, i => read());
        }

        [TestMethod]
        public void SafeRead_DoesNotUpdateStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetRandomStream();

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            int bytesRead = 0;
            long position = 0;

            while (position < stream.Length)
            {
                bytesRead += stream.SafeRead(destinationBuffer, 0, Math.Min(step, (int)stream.Length - bytesRead), ref position);
                Assert.AreEqual(bytesRead, position);
                Assert.AreEqual(0, stream.Position);
            }
        }
        #endregion

        #region SafeRead Span Tests
        [TestMethod]
        public void SafeReadSpanDoesNotUpdateStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetRandomStream();

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            int bytesRead = 0;
            long position = 0L;

            while (position < stream.Length)
            {
                bytesRead += stream.SafeRead(destinationBuffer.AsSpan(0, Math.Min(step, (int)stream.Length - bytesRead)), ref position);
                Assert.AreEqual(bytesRead, position);
                Assert.AreEqual(0, stream.Position);
            }
        }

        [TestMethod]
        public void SafeReadSpanDoesNotDependOnStreamPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            byte[] expected = new byte[step];
            int bytesRead = 0;
            long position = 0L;

            while (position < stream.Length)
            {
                stream.Position = this.random.Next(0, bufferLength);
                long lastPosition = position;
                int lastRead = stream.SafeRead(destinationBuffer.AsSpan(0, Math.Min(step, (int)stream.Length - bytesRead)), ref position);
                bytesRead += lastRead;

                Array.Copy(buffer, lastPosition, expected, 0, lastRead);

                Assert.AreEqual(bytesRead, position);
                RMSAssert.BuffersAreEqual(destinationBuffer, expected, lastRead);
            }
        }

        [TestMethod]
        public void SafeReadSpanCallsDoNotAffectOtherSafeReadCalls()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            int stepSlow = stream.MemoryManager.OptionsValue.BlockSize / 4;
            int stepFast = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] readBuffer = new byte[stepFast];
            MemoryStream readSlow = new();
            MemoryStream readFast = new();

            long positionSlow = 0L;
            long positionFast = 0L;

            while (positionFast < stream.Length)
            {
                int read = stream.SafeRead(readBuffer.AsSpan(0, stepFast), ref positionFast);
                readFast.Write(readBuffer, 0, read);
                read = stream.SafeRead(readBuffer.AsSpan(0, stepSlow), ref positionSlow);
                readSlow.Write(readBuffer, 0, read);
            }
            while (positionSlow < stream.Length)
            {
                int read = stream.SafeRead(readBuffer.AsSpan(0, stepSlow), ref positionSlow);
                readSlow.Write(readBuffer, 0, read);
            }

            RMSAssert.BuffersAreEqual(readSlow.ToArray(), buffer);
            RMSAssert.BuffersAreEqual(readFast.ToArray(), buffer);
        }

        [TestMethod]
        public void SafeReadSpanCanBeUsedInParallel()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            void read()
            {
                for (int i = 0; i < 5; i++)
                {
                    long position = this.random.Next(0, bufferLength);
                    long startPosition = position;
                    int length = this.random.Next(0, (int)(bufferLength - position));
                    byte[] readBuffer = new byte[length];
                    int bytesRead = stream.SafeRead(readBuffer.AsSpan(0, length), ref position);

                    RMSAssert.BuffersAreEqual(readBuffer, 0, buffer, (int)startPosition, bytesRead);
                }
            }

            Parallel.For(0, 5, i => read());
        }
        #endregion

        #region Read tests
        [TestMethod]
        public void ReadNullBufferThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentNullException>(() => stream.Read(null!, 0, 1));
        }

        [TestMethod]
        public void ReadNegativeOffsetThrowsException()
        {
            int bufferLength = 100;
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.Read(new byte[bufferLength], -1, 1));
        }

        [TestMethod]
        public void ReadOffsetPastEndThrowsException()
        {
            int bufferLength = 100;
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream.Read(new byte[bufferLength], bufferLength, 1));
        }

        [TestMethod]
        public void ReadNegativeCountThrowsException()
        {
            int bufferLength = 100;
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.Read(new byte[bufferLength], 0, -1));
        }

        [TestMethod]
        public void ReadCountOutOfBoundsThrowsException()
        {
            int bufferLength = 100;
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream.Read(new byte[bufferLength], 0, bufferLength + 1));
        }

        [TestMethod]
        public void ReadOffsetPlusCountLargerThanBufferThrowsException()
        {
            int bufferLength = 100;
            RecyclableMemoryStream stream1 = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream1.Read(new byte[bufferLength], bufferLength / 2, (bufferLength / 2) + 1));
            RecyclableMemoryStream stream2 = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream2.Read(new byte[bufferLength], (bufferLength / 2) + 1, bufferLength / 2));
        }

        [TestMethod]
        public void ReadSingleBlockReturnsCorrectBytesReadAndContentsAreCorrect()
        {
            this.WriteAndReadBytes(DefaultBlockSize);
        }

        [TestMethod]
        public void ReadMultipleBlocksReturnsCorrectBytesReadAndContentsAreCorrect()
        {
            this.WriteAndReadBytes(DefaultBlockSize * 2);
        }

        protected void WriteAndReadBytes(int length)
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(length);
            stream.Write(buffer, 0, buffer.Length);

            stream.Position = 0;

            byte[] newBuffer = new byte[buffer.Length];
            int amountRead = stream.Read(newBuffer, 0, (int)stream.Length);
            Assert.AreEqual(stream.Length, amountRead);
            Assert.AreEqual(buffer.Length, amountRead);

            RMSAssert.BuffersAreEqual(buffer, newBuffer, buffer.Length);
        }

        [TestMethod]
        public void ReadFromOffsetHasCorrectLengthAndContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, buffer.Length);

            stream.Position = buffer.Length / 2;
            int amountToRead = buffer.Length / 4;

            byte[] newBuffer = new byte[amountToRead];
            int amountRead = stream.Read(newBuffer, 0, amountToRead);
            Assert.AreEqual(amountToRead, amountRead);
            RMSAssert.BuffersAreEqual(buffer, buffer.Length / 2, newBuffer, 0, amountRead);
        }

        [TestMethod]
        public void ReadToOffsetHasCorrectLengthAndContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, buffer.Length);

            stream.Position = 0;
            int newBufferSize = buffer.Length / 2;
            int amountToRead = buffer.Length / 4;
            int offset = newBufferSize - amountToRead;

            byte[] newBuffer = new byte[newBufferSize];
            int amountRead = stream.Read(newBuffer, offset, amountToRead);
            Assert.AreEqual(amountToRead, amountRead);
            RMSAssert.BuffersAreEqual(buffer, 0, newBuffer, offset, amountRead);
        }

        [TestMethod]
        public void ReadFromAndToOffsetHasCorrectLengthAndContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, buffer.Length);

            stream.Position = buffer.Length / 2;
            int newBufferSize = buffer.Length / 2;
            int amountToRead = buffer.Length / 4;
            int offset = newBufferSize - amountToRead;

            byte[] newBuffer = new byte[newBufferSize];
            int amountRead = stream.Read(newBuffer, offset, amountToRead);
            Assert.AreEqual(amountToRead, amountRead);
            RMSAssert.BuffersAreEqual(buffer, buffer.Length / 2, newBuffer, offset, amountRead);
        }

        [TestMethod]
        public void ReadUpdatesPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            stream.Position = 0;

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            int bytesRead = 0;
            while (stream.Position < stream.Length)
            {
                bytesRead += stream.Read(destinationBuffer, 0, Math.Min(step, (int)stream.Length - bytesRead));
                Assert.AreEqual(bytesRead, stream.Position);
            }
        }

        [TestMethod]
        public void ReadReturnsEarlyIfLackOfData()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);

            stream.Position = bufferLength / 2;
            byte[] newBuffer = new byte[bufferLength];
            int amountRead = stream.Read(newBuffer, 0, bufferLength);
            Assert.AreEqual(bufferLength / 2, amountRead);
        }

        [TestMethod]
        public void ReadPastEndOfLargeBufferIsOk()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.MemoryManager.OptionsValue.LargeBufferMultiple;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, buffer.Length);

            // Force switch to large buffer
            stream.GetBuffer();

            stream.Position = stream.Length / 2;
            byte[] destinationBuffer = new byte[bufferLength];
            int amountRead = stream.Read(destinationBuffer, 0, destinationBuffer.Length);
            Assert.AreEqual(stream.Length / 2, amountRead);
        }

        [TestMethod]
        public void ReadFromPastEndReturnsZero()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            stream.Position = bufferLength;
            int amountRead = stream.Read(buffer, 0, bufferLength);
            Assert.AreEqual(0, amountRead);
        }
        #endregion

        #region Read tests
        [TestMethod]
        public void ReadSpanSingleBlockReturnsCorrectBytesReadAndContentsAreCorrect()
        {
            this.WriteAndReadSpanBytes(DefaultBlockSize);
        }

        [TestMethod]
        public void ReadSpanMultipleBlocksReturnsCorrectBytesReadAndContentsAreCorrect()
        {
            this.WriteAndReadSpanBytes(DefaultBlockSize * 2);
        }

        protected void WriteAndReadSpanBytes(int length)
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(length);
            stream.Write(buffer.AsSpan());

            stream.Position = 0;

            byte[] newBuffer = new byte[buffer.Length];
            int amountRead = stream.Read(newBuffer.AsSpan(0, (int)stream.Length));
            Assert.AreEqual(stream.Length, amountRead);
            Assert.AreEqual(buffer.Length, amountRead);

            RMSAssert.BuffersAreEqual(buffer, newBuffer, buffer.Length);
        }

        [TestMethod]
        public void ReadSpanFromOffsetHasCorrectLengthAndContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer.AsSpan());

            stream.Position = buffer.Length / 2;
            int amountToRead = buffer.Length / 4;

            byte[] newBuffer = new byte[amountToRead];
            int amountRead = stream.Read(newBuffer.AsSpan());
            Assert.AreEqual(amountToRead, amountRead);
            RMSAssert.BuffersAreEqual(buffer, buffer.Length / 2, newBuffer, 0, amountRead);
        }

        [TestMethod]
        public void ReadSpanToOffsetHasCorrectLengthAndContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer.AsSpan());

            stream.Position = 0;
            int newBufferSize = buffer.Length / 2;
            int amountToRead = buffer.Length / 4;
            int offset = newBufferSize - amountToRead;

            byte[] newBuffer = new byte[newBufferSize];
            int amountRead = stream.Read(newBuffer.AsSpan(offset, amountToRead));
            Assert.AreEqual(amountToRead, amountRead);
            RMSAssert.BuffersAreEqual(buffer, 0, newBuffer, offset, amountRead);
        }

        [TestMethod]
        public void ReadSpanFromAndToOffsetHasCorrectLengthAndContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer.AsSpan());

            stream.Position = buffer.Length / 2;
            int newBufferSize = buffer.Length / 2;
            int amountToRead = buffer.Length / 4;
            int offset = newBufferSize - amountToRead;

            byte[] newBuffer = new byte[newBufferSize];
            int amountRead = stream.Read(newBuffer.AsSpan(offset, amountToRead));
            Assert.AreEqual(amountToRead, amountRead);
            RMSAssert.BuffersAreEqual(buffer, buffer.Length / 2, newBuffer, offset, amountRead);
        }

        [TestMethod]
        public void ReadSpanUpdatesPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 1000000;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer.AsSpan());

            stream.Position = 0;

            int step = stream.MemoryManager.OptionsValue.BlockSize / 2;
            byte[] destinationBuffer = new byte[step];
            int bytesRead = 0;
            while (stream.Position < stream.Length)
            {
                bytesRead += stream.Read(destinationBuffer.AsSpan(0, Math.Min(step, (int)stream.Length - bytesRead)));
                Assert.AreEqual(bytesRead, stream.Position);
            }
        }

        [TestMethod]
        public void ReadSpanReturnsEarlyIfLackOfData()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer.AsSpan());

            stream.Position = bufferLength / 2;
            byte[] newBuffer = new byte[bufferLength];
            int amountRead = stream.Read(newBuffer.AsSpan());
            Assert.AreEqual(bufferLength / 2, amountRead);
        }

        [TestMethod]
        public void ReadSpanPastEndOfLargeBufferIsOk()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.MemoryManager.OptionsValue.LargeBufferMultiple;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer.AsSpan());

            // Force switch to large buffer
            stream.GetBuffer();

            stream.Position = stream.Length / 2;
            byte[] destinationBuffer = new byte[bufferLength];
            int amountRead = stream.Read(destinationBuffer.AsSpan());
            Assert.AreEqual(stream.Length / 2, amountRead);
        }

        [TestMethod]
        public void ReadSpanFromPastEndReturnsZero()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer.AsSpan());
            stream.Position = bufferLength;
            int amountRead = stream.Read(buffer.AsSpan());
            Assert.AreEqual(0, amountRead);
        }
        #endregion

        #region Capacity tests
        [TestMethod]
        public void SetCapacityRoundsUp()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int step = 51001;
            for (int i = 0; i < 100; i++)
            {
                stream.Capacity += step;
                Assert.AreEqual(0, stream.Capacity % stream.MemoryManager.OptionsValue.BlockSize);
            }
        }

        [TestMethod]
        public void DecreaseCapacityDoesNothing()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int originalCapacity = stream.Capacity;
            stream.Capacity *= 2;
            int newCapacity = stream.Capacity;
            Assert.IsTrue(stream.Capacity > originalCapacity);
            stream.Capacity /= 2;
            Assert.AreEqual(newCapacity, stream.Capacity);
        }

        [TestMethod]
        public void CapacityGoesLargeWhenLargeGetBufferCalled()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize);
            stream.Write(buffer, 0, buffer.Length);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.BlockSize, stream.Capacity);
            stream.Write(buffer, 0, buffer.Length);
            stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, stream.Capacity);
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, stream.Capacity64);
        }

        [TestMethod]
        public void EnsureCapacityOperatesOnLargeBufferWhenNeeded()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.BlockSize);
            stream.Write(buffer, 0, buffer.Length);
            stream.Write(buffer, 0, buffer.Length);
            stream.GetBuffer();

            // At this point, we're not longer using blocks, just large buffers
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, stream.Capacity);

            // this should bump up the capacity by the LargeBufferMultiple
            stream.Capacity = stream.MemoryManager.OptionsValue.LargeBufferMultiple + 1;

            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple * 2, stream.Capacity);
        }

        [TestMethod]
        public void CapacityThrowsOnTooLargeStream()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Capacity64 = (long)int.MaxValue + 1;
            Assert.AreEqual((long)int.MaxValue + 1, stream.Capacity64);
            Assert.ThrowsException<InvalidOperationException>(() => { int cap = stream.Capacity; });
        }
        #endregion

        #region SetLength Tests
        [TestMethod]
        public void SetLengthThrowsExceptionOnNegativeValue()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.SetLength(-1));
        }

        [TestMethod]
        public void SetLengthSetsLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int length = 100;
            stream.SetLength(length);
            Assert.AreEqual(length, stream.Length);
        }

        [TestMethod]
        public void SetLengthIncreasesCapacity()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int length = stream.Capacity + 1;
            stream.SetLength(length);
            Assert.IsTrue(stream.Capacity >= stream.Length);
        }

        [TestMethod]
        public void SetLengthCanSetPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int length = 100;
            stream.SetLength(length);
            stream.Position = length / 2;
            Assert.AreEqual(length / 2, stream.Position);
        }

        [TestMethod]
        public void SetLengthDoesNotResetPositionWhenGrowing()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            stream.Position = bufferLength / 4;
            stream.SetLength(bufferLength / 2);
            Assert.AreEqual(bufferLength / 4, stream.Position);
        }

        [TestMethod]
        public void SetLengthMovesPositionToBeInBounds()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            Assert.AreEqual(bufferLength, stream.Position);
            stream.SetLength(bufferLength / 2);
            Assert.AreEqual(bufferLength / 2, stream.Length);
            Assert.AreEqual(stream.Length, stream.Position);
        }
        #endregion

        #region ToString Tests
        [TestMethod]
        public void ToStringReturnsHelpfulDebugInfo()
        {
            string tag = "Unit test";
            RecyclableMemoryStream stream = new(this.GetMemoryManager(), tag);
            byte[] buffer = this.GetRandomBuffer(1000);
            stream.Write(buffer, 0, buffer.Length);
            string debugInfo = stream.ToString();

            StringAssert.Contains(debugInfo, stream.Id.ToString());
            StringAssert.Contains(debugInfo, tag);
            StringAssert.Contains(debugInfo, buffer.Length.ToString("N0"));
        }

        [TestMethod]
        public void ToStringWithNullTagIsOk()
        {
            RecyclableMemoryStream stream = new(this.GetMemoryManager(), null);
            byte[] buffer = this.GetRandomBuffer(1000);
            stream.Write(buffer, 0, buffer.Length);
            string debugInfo = stream.ToString();

            StringAssert.Contains(debugInfo, stream.Id.ToString());
            StringAssert.Contains(debugInfo, buffer.Length.ToString("N0"));
        }

        [TestMethod]
        public void ToStringOnDisposedOk()
        {
            Guid guid = Guid.NewGuid();
            string tag = "NUnit";
            RecyclableMemoryStream stream = new(this.GetMemoryManager(), guid, tag);
            stream.WriteByte(1);
            stream.Dispose();
            Assert.AreEqual($"Disposed: Id = {guid}, Tag = {tag}, Final Length: 1 bytes", stream.ToString());
        }
        #endregion

        #region ToArray Tests
        [TestMethod]
        public void ToArrayReturnsDifferentBufferThanGetBufferWithSameContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = 100;
            byte[] buffer = this.GetRandomBuffer(bufferLength);

            stream.Write(buffer, 0, bufferLength);

            byte[] getBuffer = stream.GetBuffer();
            byte[] toArrayBuffer = stream.ToArray();
            Assert.AreNotSame(getBuffer, toArrayBuffer);
            RMSAssert.BuffersAreEqual(toArrayBuffer, getBuffer, bufferLength);
        }

        [TestMethod]
        public void ToArrayWithLargeBufferReturnsDifferentBufferThanGetBufferWithSameContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.MemoryManager.OptionsValue.BlockSize * 2;
            byte[] buffer = this.GetRandomBuffer(bufferLength);

            stream.Write(buffer, 0, bufferLength);

            byte[] getBuffer = stream.GetBuffer();
            byte[] toArrayBuffer = stream.ToArray();
            Assert.AreNotSame(getBuffer, toArrayBuffer);
            RMSAssert.BuffersAreEqual(toArrayBuffer, getBuffer, bufferLength);
        }

        [TestMethod]
        public void ToArrayThrowsExceptionWhenConfigured()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.MemoryManager.OptionsValue.BlockSize * 2;
            byte[] buffer = this.GetRandomBuffer(bufferLength);

            stream.Write(buffer, 0, bufferLength);

            // Ensure default is false => does not throw
            _ = stream.ToArray();

            stream.MemoryManager.OptionsValue.ThrowExceptionOnToArray = true;
            Assert.ThrowsException<NotSupportedException>(() => stream.ToArray());

            stream.MemoryManager.OptionsValue.ThrowExceptionOnToArray = false;
            // Doesn't throw again
            _ = stream.ToArray();
        }
        #endregion

        #region CanRead, CanSeek, etc. Tests
        [TestMethod]
        public void CanSeekIsTrue()
        {
            Assert.IsTrue(this.GetDefaultStream().CanSeek);
        }

        [TestMethod]
        public void CanReadIsTrue()
        {
            Assert.IsTrue(this.GetDefaultStream().CanRead);
        }

        [TestMethod]
        public void CanWriteIsTrue()
        {
            Assert.IsTrue(this.GetDefaultStream().CanWrite);
        }

        [TestMethod]
        public void CanTimeoutIsFalse()
        {
            Assert.IsFalse(this.GetDefaultStream().CanTimeout);
        }
        #endregion

        #region Seek Tests
        [TestMethod]
        public void SeekFromBeginToBeforeBeginThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
        }

        [TestMethod]
        public void SeekFromCurrentToBeforeBeginThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<IOException>(() => stream.Seek(-1, SeekOrigin.Current));
        }

        [TestMethod]
        public void SeekFromEndToBeforeBeginThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<IOException>(() => stream.Seek(-1, SeekOrigin.End));
        }

        [TestMethod]
        public void SeekWithBadOriginThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentException>(() => stream.Seek(1, (SeekOrigin)99));
        }

        [TestMethod]
        public void SeekPastEndOfStreamHasCorrectPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int expected = 100;
            stream.Seek(expected, SeekOrigin.Begin);
            Assert.AreEqual(expected, stream.Position);
            Assert.AreEqual(0, stream.Length);
        }

        [TestMethod]
        public void SeekFromBeginningHasCorrectPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int position = 100;
            stream.Seek(position, SeekOrigin.Begin);
            Assert.AreEqual(position, stream.Position);
        }

        [TestMethod]
        public void SeekFromCurrentHasCorrectPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int position = 100;
            stream.Seek(position, SeekOrigin.Begin);
            Assert.AreEqual(position, stream.Position);

            stream.Seek(-100, SeekOrigin.Current);
            Assert.AreEqual(0, stream.Position);
        }

        [TestMethod]
        public void SeekFromEndHasCorrectPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int length = 100;
            stream.SetLength(length);

            stream.Seek(-1, SeekOrigin.End);
            Assert.AreEqual(length - 1, stream.Position);
        }

        [TestMethod]
        public void SeekPastEndAndWriteHasCorrectLengthAndPosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int position = 100;
            const int bufferLength = 100;
            stream.Seek(position, SeekOrigin.Begin);
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, bufferLength);
            Assert.AreEqual(position + bufferLength, stream.Length);
            Assert.AreEqual(position + bufferLength, stream.Position);
        }
        #endregion

        #region Position Tests
        [TestMethod]
        public void PositionSetToNegativeThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.Position = -1);
        }

        [TestMethod]
        public void PositionSetToAnyValue()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int maxValue = int.MaxValue;
            int step = maxValue / 32;
            for (long i = 0; i < maxValue; i += step)
            {
                stream.Position = i;
                Assert.AreEqual(i, stream.Position);
            }
        }
        #endregion

        #region Advance Tests
        [TestMethod]
        public void AdvanceByNegativeThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Position = 10;
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.Advance(-1));
        }

        [TestMethod]
        public void AdvancePastLargerThanMaxStreamLengthThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Advance(1);
            Assert.ThrowsException<InvalidOperationException>(() => stream.Advance(int.MaxValue));
        }

        [TestMethod]
        public void AdvancePastTempBufferLengthThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Memory<byte> memory = stream.GetMemory(stream.MemoryManager.OptionsValue.BlockSize + 1);
            Assert.ThrowsException<InvalidOperationException>(() => stream.Advance(memory.Length + 1));
        }

        [TestMethod]
        public void AdvancePastBlockLengthThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Memory<byte> memory = stream.GetMemory();
            Assert.ThrowsException<InvalidOperationException>(() => stream.Advance(memory.Length + 1));
        }

        [TestMethod]
        public void AdvancePastLargeBufferLengthThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Position = stream.MemoryManager.OptionsValue.BlockSize + 1;
            stream.GetBuffer();
            Memory<byte> memory = stream.GetMemory();
            Assert.ThrowsException<InvalidOperationException>(() => stream.Advance(memory.Length + 1));
        }

        [TestMethod]
        public void AdvanceToAnyValue()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int maxValue = int.MaxValue;
            int step = maxValue / 32;
            for (int i = 1; i <= 32; i++)
            {
                stream.GetSpan(step);
                stream.Advance(step);
                Assert.AreEqual(i * step, stream.Position);
                Assert.AreEqual(i * step, stream.Length);
            }

            stream.Position = 0;
            for (int i = 1; i <= 32; i++)
            {
                stream.GetSpan(step);
                stream.Advance(step);
                Assert.AreEqual(i * step, stream.Position);
                Assert.AreEqual(32 * step, stream.Length);
            }
        }

        [TestMethod]
        public void AdvanceOverTempBufferMakesWritesVisible()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(32);
            Memory<byte> memory = stream.GetMemory(stream.MemoryManager.OptionsValue.BlockSize + 1);
            buffer.CopyTo(memory);
            CollectionAssert.AreNotEqual(buffer, stream.GetBuffer().AsMemory(0, 32).ToArray());
            stream.Advance(buffer.Length);
            CollectionAssert.AreEqual(buffer, stream.GetBuffer().AsMemory(0, 32).ToArray());
        }

        [TestMethod]
        public void AdvanceOverReplacedTempBufferDoesNotMakeWritesVisible()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer1 = this.GetRandomBuffer(32);
            byte[] buffer2 = this.GetRandomBuffer(32);
            Memory<byte> memory = stream.GetMemory(stream.MemoryManager.OptionsValue.BlockSize + 1);
            buffer1.CopyTo(memory);
            memory = stream.GetMemory(stream.MemoryManager.OptionsValue.BlockSize + 1);
            buffer2.CopyTo(memory);
            stream.Advance(buffer2.Length);
            CollectionAssert.AreNotEqual(buffer1, stream.GetBuffer().AsMemory(0, 32).ToArray());
            CollectionAssert.AreEqual(buffer2, stream.GetBuffer().AsMemory(0, 32).ToArray());
        }
        #endregion

        #region Dispose and Pooling Tests
        [TestMethod]
        public void Pooling_NewMemoryManagerHasZeroFreeAndInUseBytes()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.LargePoolFreeSize);

            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
            Assert.AreEqual(0, memMgr.LargePoolInUseSize);
        }

        [TestMethod]
        public void Pooling_NewStreamIncrementsInUseBytes()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);

            RecyclableMemoryStream stream = new(memMgr);
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, stream.Capacity);
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, memMgr.SmallPoolInUseSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
        }

        [TestMethod]
        public void Pooling_DisposeOneBlockAdjustsInUseAndFreeBytes()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            Assert.AreEqual(stream.Capacity, stream.MemoryManager.SmallPoolInUseSize);

            stream.Dispose();
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, memMgr.SmallPoolFreeSize);
        }

        [TestMethod]
        public void Pooling_DisposeMultipleBlocksAdjustsInUseAndFreeBytes()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int bufferLength = stream.MemoryManager.OptionsValue.BlockSize * 4;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(bufferLength, stream.MemoryManager.SmallPoolInUseSize);
            Assert.AreEqual(0, stream.MemoryManager.SmallPoolFreeSize);
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            stream.Dispose();

            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
            Assert.AreEqual(bufferLength, memMgr.SmallPoolFreeSize);
        }

        [TestMethod]
        public void Pooling_DisposingFreesBlocks()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int numBlocks = 4;
            int bufferLength = stream.MemoryManager.OptionsValue.BlockSize * numBlocks;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, buffer.Length);
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            stream.Dispose();
            Assert.AreEqual(numBlocks, memMgr.SmallBlocksFree);
        }

        [TestMethod]
        public void DisposeReturnsLargeBuffer()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            const int numBlocks = 4;
            int bufferLength = stream.MemoryManager.OptionsValue.BlockSize * numBlocks;
            byte[] buffer = this.GetRandomBuffer(bufferLength);
            stream.Write(buffer, 0, buffer.Length);
            byte[] newBuffer = stream.GetBuffer();
            Assert.AreEqual(stream.MemoryManager.OptionsValue.LargeBufferMultiple, newBuffer.Length);

            Assert.AreEqual(0, stream.MemoryManager.LargeBuffersFree);
            Assert.AreEqual(0, stream.MemoryManager.LargePoolFreeSize);
            Assert.AreEqual(newBuffer.Length, stream.MemoryManager.LargePoolInUseSize);
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            stream.Dispose();
            Assert.AreEqual(1, memMgr.LargeBuffersFree);
            Assert.AreEqual(newBuffer.Length, memMgr.LargePoolFreeSize);
            Assert.AreEqual(0, memMgr.LargePoolInUseSize);
        }

        [TestMethod]
        public void DisposeTwiceDoesNotThrowException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Dispose();
            stream.Dispose();
        }

        [TestMethod]
        public void DisposeReturningATooLargeBufferGetsDropped()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            int bufferSize = stream.MemoryManager.OptionsValue.MaximumBufferSize + 1;
            byte[] buffer = this.GetRandomBuffer(bufferSize);
            stream.Write(buffer, 0, buffer.Length);
            byte[] newBuffer = stream.GetBuffer();
            Assert.AreEqual(newBuffer.Length, stream.MemoryManager.LargePoolInUseSize);
            Assert.AreEqual(0, stream.MemoryManager.LargePoolFreeSize);
            stream.Dispose();
            Assert.AreEqual(0, memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
        }

        [TestMethod]
        public void AccessingObjectAfterDisposeThrowsObjectDisposedException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Dispose();

            byte[] buffer = new byte[100];

            Assert.IsFalse(stream.CanRead);
            Assert.IsFalse(stream.CanSeek);
            Assert.IsFalse(stream.CanWrite);

            Assert.ThrowsException<ObjectDisposedException>(() => { int x = stream.Capacity; });
            Assert.ThrowsException<ObjectDisposedException>(() => { long x = stream.Length; });
            Assert.ThrowsException<ObjectDisposedException>(() => { RecyclableMemoryStreamManager x = stream.MemoryManager; });
            Assert.ThrowsException<ObjectDisposedException>(() => { Guid x = stream.Id; });
            Assert.ThrowsException<ObjectDisposedException>(() => { string x = stream.Tag; });
            Assert.ThrowsException<ObjectDisposedException>(() => { long x = stream.Position; });
            Assert.ThrowsException<ObjectDisposedException>(() => { int x = stream.ReadByte(); });
            Assert.ThrowsException<ObjectDisposedException>(() => { int x = stream.Read(buffer, 0, buffer.Length); });
            Assert.ThrowsException<ObjectDisposedException>(() => stream.WriteByte(255));
            Assert.ThrowsException<ObjectDisposedException>(() => stream.Write(buffer, 0, buffer.Length));
            Assert.ThrowsException<ObjectDisposedException>(() => stream.SetLength(100));
            Assert.ThrowsException<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
            Assert.ThrowsException<ObjectDisposedException>(() => { byte[] x = stream.GetBuffer(); });
        }

        [TestMethod]
        public void DisposeReportsStreamLength()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.WriteByte(255);
            bool handlerTriggered = false;
            stream.MemoryManager.StreamLength += (obj, args) =>
            {
                Assert.AreEqual(1, args.Length);
                handlerTriggered = true;
            };
            stream.Dispose();
            Assert.IsTrue(handlerTriggered);
        }

        [TestMethod]
        [DoNotParallelize]
        public void FinalizedStreamTriggersEvent()
        {
            bool handlerTriggered = false;
            Guid expectedGuid = Guid.NewGuid();

            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();

            mgr.StreamFinalized += (obj, args) =>
            {
                Assert.AreEqual("Tag", args.Tag);
                Assert.AreEqual(expectedGuid, args.Id);
                Assert.IsTrue(string.IsNullOrEmpty(args.AllocationStack));
                handlerTriggered = true;
            };

            mgr.StreamDisposed += (obj, args) => Assert.IsTrue(args.Lifetime >= TimeSpan.Zero);

            CreateDeadStream(mgr, expectedGuid, "Tag");
            Thread.Sleep(100);

            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();

            Assert.IsTrue(handlerTriggered);

            static void CreateDeadStream(RecyclableMemoryStreamManager mgr, Guid expectedGuid, string tag)
            {
                _ = new RecyclableMemoryStream(mgr, expectedGuid, tag);
            }
        }

        private void MemoryManager_StreamLength(object sender, RecyclableMemoryStreamManager.StreamLengthEventArgs e)
        {
            throw new NotImplementedException();
        }
        #endregion

        #region GetStream tests
        [TestMethod]
        public void GetStreamReturnsADefaultStream()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            RecyclableMemoryStream stream = memMgr.GetStream();
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, stream.Capacity);
        }

        [TestMethod]
        public void GetStreamWithTag()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            string tag = "MyTag";
            RecyclableMemoryStream stream = memMgr.GetStream(tag);
            Assert.AreEqual(memMgr.OptionsValue.BlockSize, stream.Capacity);
            Assert.AreEqual(tag, stream.Tag);
        }

        [TestMethod]
        public void GetStreamWithTagAndRequiredSize()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            string tag = "MyTag";
            int requiredSize = 13131313;
            RecyclableMemoryStream stream = memMgr.GetStream(tag, requiredSize);
            Assert.IsTrue(stream.Capacity >= requiredSize);
            Assert.AreEqual(tag, stream.Tag);
        }

        [TestMethod]
        public void GetStreamWithTagAndRequiredSizeAndContiguousBuffer()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            string tag = "MyTag";
            int requiredSize = 13131313;

            RecyclableMemoryStream stream = memMgr.GetStream(tag, requiredSize, false);
            Assert.IsTrue(stream.Capacity >= requiredSize);
            Assert.AreEqual(tag, stream.Tag);

            stream = memMgr.GetStream(tag, requiredSize, true);
            Assert.IsTrue(stream.Capacity >= requiredSize);
            Assert.AreEqual(tag, stream.Tag);
        }

        [TestMethod]
        public void GetStreamWithBuffer()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = this.GetRandomBuffer(1000);
            string tag = "MyTag";

            RecyclableMemoryStream stream = memMgr.GetStream(tag, buffer, 1, buffer.Length - 1);
            RMSAssert.BuffersAreEqual(buffer, 1, stream.GetBuffer(), 0, buffer.Length - 1);
            Assert.AreNotSame(buffer, stream.GetBuffer());
            Assert.AreEqual(tag, stream.Tag);
        }

        [TestMethod]
        public void GetStreamWithOnlyBuffer()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = this.GetRandomBuffer(1000);

            RecyclableMemoryStream stream = memMgr.GetStream(buffer);
            RMSAssert.BuffersAreEqual(buffer, 0, stream.GetBuffer(), 0, buffer.Length);
            Assert.AreNotSame(buffer, stream.GetBuffer());
        }

        [TestMethod]
        public void GetStreamWithReadOnlySpan()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            ReadOnlyMemory<byte> buffer = new(this.GetRandomBuffer(1000));
            ReadOnlyMemory<byte> bufferSlice = buffer[1..];
            string tag = "MyTag";

            RecyclableMemoryStream stream = memMgr.GetStream(tag, bufferSlice.Span);
            RMSAssert.BuffersAreEqual(bufferSlice.Span, stream.GetBuffer(), bufferSlice.Length);
            Assert.AreEqual(tag, stream.Tag);
        }

        [TestMethod]
        public void GetStreamWithOnlyReadOnlySpan()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            ReadOnlyMemory<byte> buffer = new(this.GetRandomBuffer(1000));

            RecyclableMemoryStream stream = memMgr.GetStream(buffer.Span);
            RMSAssert.BuffersAreEqual(buffer.Span, stream.GetBuffer(), buffer.Length);
        }
        #endregion

        #region WriteTo tests
        [TestMethod]
        public void WriteToNullStreamThrowsException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentNullException>(() => stream.WriteTo(null!));
        }

        [TestMethod]
        public void WriteToOtherStreamHasEqualContents()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, buffer.Length);

            RecyclableMemoryStream stream2 = this.GetDefaultStream();
            stream.WriteTo(stream2);

            Assert.AreEqual(stream.Length, stream2.Length);
            RMSAssert.BuffersAreEqual(buffer, stream2.GetBuffer(), buffer.Length);
        }

        [TestMethod]
        public void WriteToOtherStreamDoesNotChangePosition()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = buffer.Length / 2;

            RecyclableMemoryStream stream2 = this.GetDefaultStream();
            stream.WriteTo(stream2);
            Assert.AreEqual(buffer.Length / 2, stream.Position);

            Assert.AreEqual(stream.Length, stream2.Length);
            RMSAssert.BuffersAreEqual(buffer, stream2.GetBuffer(), buffer.Length);
        }

        [DataRow(DefaultBlockSize / 2)]
        [DataRow(DefaultBlockSize * 2)]
        public void WriteToOtherStreamOffsetCountHasEqualContentsSubStream(int bufferSize)
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(bufferSize);
            stream.Write(buffer, 0, buffer.Length);

            // force to large buffers if applicable
            stream.GetBuffer();

            RecyclableMemoryStream stream2 = this.GetDefaultStream();
            int offset = bufferSize / 2;
            int length = bufferSize / 4;

            stream.WriteTo(stream2, offset, length);

            Assert.AreEqual(length, stream2.Length);
            RMSAssert.BuffersAreEqual(buffer, offset, stream2.GetBuffer(), 0, length);
        }

        [DataRow(DefaultBlockSize / 2)]
        [DataRow(DefaultBlockSize * 2)]
        public void WriteToOtherStreamOffsetCountHasEqualContentsFullStream(int bufferSize)
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(bufferSize);
            stream.Write(buffer, 0, buffer.Length);

            // force to large buffers if applicable
            stream.GetBuffer();

            RecyclableMemoryStream stream2 = this.GetDefaultStream();
            int offset = 0;
            int length = buffer.Length;

            stream.WriteTo(stream2, offset, length);

            Assert.AreEqual(length, stream2.Length);
            RMSAssert.BuffersAreEqual(buffer, offset, stream2.GetBuffer(), 0, length);
        }

        [TestMethod]
        public void WriteToOtherStreamOffsetCountThrowException()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentNullException>(() => stream.WriteTo((Stream)null!, 0, (int)stream.Length));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(stream, -1, (int)stream.Length));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(stream, 1, (int)stream.Length));
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(stream, 0, (int)stream.Length + 1));
        }

        [TestMethod]
        public void WriteToByteArray_NullTarget()
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentNullException>(() => stream.WriteTo(null!));
        }

        [TestMethod]
        public void WriteToByteArray_FullArray_Small()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            stream.WriteTo(targetBuffer);
            RMSAssert.BuffersAreEqual(sourceBuffer, targetBuffer);
        }

        [TestMethod]
        public void WriteToByteArrayDoesNotChangePosition()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            stream.Position = sourceBuffer.Length / 2;
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            stream.WriteTo(targetBuffer);
            Assert.AreEqual(sourceBuffer.Length / 2, stream.Position);
            RMSAssert.BuffersAreEqual(sourceBuffer, targetBuffer);
        }

        [TestMethod]
        public void WriteToByteArray_Full_Array_Large()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(25 * DefaultBlockSize);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            stream.GetBuffer();
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            stream.WriteTo(targetBuffer);
            RMSAssert.BuffersAreEqual(sourceBuffer, targetBuffer);
        }

        [TestMethod]
        public void WriteToByteArray_OffsetCount()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            stream.WriteTo(targetBuffer, sourceBuffer.Length / 2, sourceBuffer.Length / 2);
            RMSAssert.BuffersAreEqual(sourceBuffer, sourceBuffer.Length / 2, targetBuffer, 0, sourceBuffer.Length / 2);
        }

        [TestMethod]
        public void WriteToByteArray_CountLargerThanSourceWithZeroOffset()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, 0, sourceBuffer.Length + 1));
        }

        [TestMethod]
        public void WriteToByteArray_CountLargerThanSourceWithNonZeroOffset()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, 1, sourceBuffer.Length));
        }

        [TestMethod]
        public void WriteToByteArray_CountLargerThanTargetZeroOffset()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, 0, sourceBuffer.Length, 1));
        }

        [TestMethod]
        public void WriteToByteArray_CountLargerThanTargetNonZeroOffset()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, 1, sourceBuffer.Length - 1, 2));
        }

        [TestMethod]
        public void WriteToByteArray_TargetOffsetLargerThanTarget()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, 0, 1, sourceBuffer.Length));
        }

        [TestMethod]
        public void WriteToByteArray_NegativeOffsetThrowsException()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, -1, sourceBuffer.Length));
        }


        [TestMethod]
        public void WriteToByteArray_NegativeTargetOffsetThrowsException()
        {
            byte[] sourceBuffer = this.GetRandomBuffer(100);
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.Write(sourceBuffer);
            byte[] targetBuffer = new byte[sourceBuffer.Length];
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => stream.WriteTo(targetBuffer, 0, sourceBuffer.Length, -1));
        }


        #endregion

        #region MaximumStreamCapacityBytes Tests
        [TestMethod]
        public void MaximumStreamCapacity_NoLimit()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.MemoryManager.OptionsValue.MaximumStreamCapacity = 0;
            stream.Capacity = (DefaultMaximumBufferSize * 2) + 1;
            Assert.IsTrue(stream.Capacity >= (DefaultMaximumBufferSize * 2) + 1);
        }

        [TestMethod]
        public void MaximumStreamCapacity_Limit()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int maxCapacity = DefaultMaximumBufferSize * 2;
            stream.MemoryManager.OptionsValue.MaximumStreamCapacity = maxCapacity;
            stream.Capacity = maxCapacity;
            Assert.ThrowsException<OutOfMemoryException>(() => stream.Capacity = maxCapacity + 1);
        }

        [TestMethod]
        public void MaximumStreamCapacity_StreamUnchanged()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int maxCapacity = DefaultMaximumBufferSize * 2;
            stream.MemoryManager.OptionsValue.MaximumStreamCapacity = maxCapacity;
            stream.Capacity = maxCapacity;
            int oldCapacity = stream.Capacity;
            Assert.ThrowsException<OutOfMemoryException>(() => stream.Capacity = maxCapacity + 1);
            Assert.AreEqual(oldCapacity, stream.Capacity);
        }

        [TestMethod]
        public void MaximumStreamCapacity_StreamUnchangedAfterWriteOverLimit()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            int maxCapacity = DefaultMaximumBufferSize * 2;
            stream.MemoryManager.OptionsValue.MaximumStreamCapacity = maxCapacity;
            byte[] buffer1 = this.GetRandomBuffer(100);
            stream.Write(buffer1, 0, buffer1.Length);
            long oldLength = stream.Length;
            int oldCapacity = stream.Capacity;
            long oldPosition = stream.Position;
            byte[] buffer2 = this.GetRandomBuffer(maxCapacity);
            Assert.ThrowsException<OutOfMemoryException>(() => stream.Write(buffer2, 0, buffer2.Length));
            Assert.AreEqual(oldLength, stream.Length);
            Assert.AreEqual(oldCapacity, stream.Capacity);
            Assert.AreEqual(oldPosition, stream.Position);
        }
        #endregion

        #region CopyTo Tests

        [TestMethod]
        public void CopyTo()
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(100);
            stream.Write(buffer);

            using MemoryStream memoryStream = new();
            stream.Position = 0;
            stream.CopyTo(memoryStream, 100);

            byte[] destinationBuffer = memoryStream.GetBuffer();

            RMSAssert.BuffersAreEqual(destinationBuffer, buffer, 100);
        }

        #endregion

        #region CopyToAsync Tests
        [TestMethod]
        public void CopyToAsyncThrowsOnNullDestination()
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            Assert.ThrowsException<ArgumentNullException>(() => stream.CopyToAsync(null!, DefaultBlockSize, CancellationToken.None));
        }

        [TestMethod]
        public void CopyToAsyncThrowsIfDisposed()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            using RecyclableMemoryStream otherStream = this.GetDefaultStream();
            stream.Dispose();
            Assert.ThrowsException<ObjectDisposedException>(() => stream.CopyToAsync(otherStream, DefaultBlockSize, CancellationToken.None));
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncSmallerThanBlock(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize / 2);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = offset;
            RecyclableMemoryStream otherStream = this.GetDefaultStream();
            stream.CopyToAsync(otherStream).Wait();
            Assert.AreEqual(stream.Length - offset, otherStream.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherStream.GetBuffer(), buffer.Length - offset);
        }

        [TestMethod]
        public void CopyToAsyncZeroBlocks()
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            RecyclableMemoryStream otherStream = this.GetDefaultStream();
            stream.CopyToAsync(otherStream).Wait();
            Assert.AreEqual(0, otherStream.Length);
        }

        [TestMethod]
        public void CopyToAsyncZeroBlocksNonMemoryStream()
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            string filename = Path.GetRandomFileName();
            using (FileStream fileStream = new(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, DefaultBlockSize, FileOptions.Asynchronous))
            {
                stream.CopyToAsync(fileStream).Wait();
            }
            byte[] otherBuffer = File.ReadAllBytes(filename);
            CollectionAssert.AreEqual(Array.Empty<byte>(), otherBuffer);
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncOneBlock(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = offset;
            RecyclableMemoryStream otherStream = this.GetDefaultStream();
            stream.CopyToAsync(otherStream).Wait();
            Assert.AreEqual(stream.Length - offset, otherStream.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherStream.GetBuffer(), buffer.Length - offset);
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncOneBlockNonMemoryStream(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = offset;
            string filename = Path.GetRandomFileName();
            using (FileStream fileStream = new(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, DefaultBlockSize, FileOptions.Asynchronous))
            {
                stream.CopyToAsync(fileStream).Wait();
            }
            byte[] otherBuffer = File.ReadAllBytes(filename);
            Assert.AreEqual(stream.Length - offset, otherBuffer.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherBuffer, buffer.Length - offset);
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncMultipleBlocks(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize * 25);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = offset;
            RecyclableMemoryStream otherStream = this.GetDefaultStream();
            stream.CopyToAsync(otherStream).Wait();
            Assert.AreEqual(stream.Length - offset, otherStream.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherStream.GetBuffer(), buffer.Length - offset);
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncMultipleBlocksNonMemoryStream(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize * 25);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = offset;
            string filename = Path.GetRandomFileName();
            using (FileStream fileStream = new(filename, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, DefaultBlockSize, FileOptions.Asynchronous))
            {
                stream.CopyToAsync(fileStream).Wait();
            }
            byte[] otherBuffer = File.ReadAllBytes(filename);
            Assert.AreEqual(stream.Length - offset, otherBuffer.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherBuffer, buffer.Length - offset);
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncLargeBuffer(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize * 25);
            stream.Write(buffer, 0, buffer.Length);
            RecyclableMemoryStream otherStream = this.GetDefaultStream();
            stream.Position = offset;
            stream.GetBuffer();
            stream.CopyToAsync(otherStream).Wait();
            Assert.AreEqual(stream.Length - offset, otherStream.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherStream.GetBuffer(), buffer.Length - offset);
        }

        [DataRow(0)]
        [DataRow(100)]
        [TestMethod]
        public void CopyToAsyncLargeBufferNonMemoryStream(int offset)
        {
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(DefaultBlockSize * 25);
            stream.Write(buffer, 0, buffer.Length);
            stream.GetBuffer();
            stream.Position = offset;
            string filename = Path.GetRandomFileName();
            using (FileStream fileStream = new(filename, FileMode.Create, FileAccess.Write))
            {
                stream.CopyToAsync(fileStream).Wait();
            }
            byte[] otherBuffer = File.ReadAllBytes(filename);
            Assert.AreEqual(stream.Length - offset, otherBuffer.Length);
            RMSAssert.BuffersAreEqual(new ReadOnlySpan<byte>(stream.GetBuffer(), offset, buffer.Length - offset), otherBuffer, buffer.Length - offset);
        }

        [DataRow(true, false)]
        [DataRow(false, false)]
        [DataRow(true, true)]
        [DataRow(false, true)]
        [TestMethod]
        [DoNotParallelize]
        public void CopyToAsyncChangesSourcePosition(bool fileStreamTarget, bool largeBuffer)
        {
            using Stream targetStream = fileStreamTarget ? File.OpenWrite(Path.GetRandomFileName()) : new MemoryStream();
            using RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(largeBuffer ? DefaultBlockSize * 25 : 100);
            stream.Write(buffer);
            Assert.AreEqual(buffer.Length, stream.Position);
            stream.Position = buffer.Length / 2;
            stream.CopyToAsync(targetStream).Wait();
            Assert.AreEqual(buffer.Length / 2, targetStream.Length);
            Assert.AreEqual(buffer.Length, stream.Position);
        }

        #endregion

        #region Very Large Buffer Tests (> 2 GB)
        [TestMethod]
        public void VeryLargeStream_Write()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            Assert.IsTrue(stream.Capacity64 >= DefaultVeryLargeStreamSize);
            byte[] buffer = this.GetRandomBuffer(1 << 20);
            while (stream.Length < DefaultVeryLargeStreamSize)
            {
                stream.Write(buffer);
            }

            Assert.AreEqual(DefaultVeryLargeStreamSize, stream.Length);

            // It takes a VERY long time to check 3 GB byte-by-byte, so
            // just check final 100 MB
            byte[] checkBuffer = new byte[buffer.Length];
            stream.Seek(-checkBuffer.Length, SeekOrigin.End);
            stream.Read(checkBuffer, 0, checkBuffer.Length);

            RMSAssert.BuffersAreEqual(buffer, checkBuffer, buffer.Length);
        }

        [TestMethod]
        public void VeryLargeStream_WriteOffsetCount()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            Assert.IsTrue(stream.Capacity64 >= DefaultVeryLargeStreamSize);
            byte[] buffer = this.GetRandomBuffer(1 << 20);
            while (stream.Length < DefaultVeryLargeStreamSize)
            {
                stream.Write(buffer, 0, buffer.Length);
            }

            Assert.AreEqual(DefaultVeryLargeStreamSize, stream.Length);

            // It takes a VERY long time to check 3 GB byte-by-byte, so
            // just check final 100 MB
            byte[] checkBuffer = new byte[buffer.Length];
            stream.Seek(-checkBuffer.Length, SeekOrigin.End);
            stream.Read(checkBuffer, 0, checkBuffer.Length);

            RMSAssert.BuffersAreEqual(buffer, checkBuffer, buffer.Length);
        }

        [TestMethod]
        public void VeryLargeStream_SetLength()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            stream.SetLength(DefaultVeryLargeStreamSize);
            Assert.AreEqual(DefaultVeryLargeStreamSize, stream.Length);
            Assert.IsTrue(stream.Capacity64 >= DefaultVeryLargeStreamSize);
            stream.SetLength(DefaultVeryLargeStreamSize * 2);
            Assert.AreEqual(2 * DefaultVeryLargeStreamSize, stream.Length);
            Assert.IsTrue(stream.Capacity64 >= 2 * DefaultVeryLargeStreamSize);
        }

        [TestMethod]
        public void VeryLargeStream_ExistingLargeBufferThrowsOnMultiGBLength()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] data = this.GetRandomBuffer(1 << 20);
            stream.Write(data);
            byte[] buffer = stream.GetBuffer();
            Assert.ThrowsException<OutOfMemoryException>(() => stream.SetLength(DefaultVeryLargeStreamSize));
        }

        [TestMethod]
        public void VeryLargeStream_GetBufferThrows()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            Assert.ThrowsException<OutOfMemoryException>(() => stream.GetBuffer());
        }

        [TestMethod]
        public void VeryLargeStream_SetPositionThrowsIfLargeBuffer()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetDefaultStream();
            stream.SetLength(1 << 20);
            byte[] buffer = stream.GetBuffer();
            Assert.ThrowsException<InvalidOperationException>(() => stream.Position = DefaultVeryLargeStreamSize);
        }

        [TestMethod]
        public void VeryLargeStream_WriteByte()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            byte[] buffer = new byte[100 << 20];
            while (stream.Length < DefaultVeryLargeStreamSize)
            {
                stream.Write(buffer);
            }

            long startingLength = stream.Length;
            Assert.IsTrue(startingLength > int.MaxValue);
            stream.WriteByte(13);
            Assert.AreEqual(startingLength + 1, stream.Length);
        }

        [TestMethod]
        public void VeryLargeStream_GetReadOnlySequence()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            RecyclableMemoryStream stream = this.GetMultiGBStream();
            byte[] buffer = new byte[100 << 20];
            while (stream.Length < DefaultVeryLargeStreamSize)
            {
                stream.Write(buffer);
            }

            SequenceReader<byte> sequence = new(stream.GetReadOnlySequence());
            Assert.AreEqual(stream.Length, sequence.Length);

            while (sequence.Remaining != 0)
            {
                Assert.IsTrue(sequence.IsNext(buffer, true));
            }
        }

        private RecyclableMemoryStream GetMultiGBStream()
        {
            if (this.ZeroOutBuffer)
            {
                Assert.Inconclusive("Disable test due to increased memory consumption that currently does not work with the hardware limits of the GitHub runners.");
            }
            return new RecyclableMemoryStream(this.GetMemoryManager(), "GetMultiGBStream", DefaultVeryLargeStreamSize);
        }

        #endregion

        #region Event Tests
        [TestMethod]
        public void EventStreamCreated()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            bool raised = false;
            mgr.StreamCreated += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.AreEqual(13, args.RequestedSize);
                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventStreamDisposed()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.GenerateCallStacks = true;
            bool raised = false;
            mgr.StreamDisposed += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.IsTrue(args.Lifetime > TimeSpan.Zero, $"TicksPerSecond: {TimeSpan.TicksPerSecond}, Freq: {Stopwatch.Frequency} Div:{TimeSpan.TicksPerSecond / Stopwatch.Frequency}");
                Assert.IsTrue(args.Lifetime < TimeSpan.FromSeconds(2));
                StringAssert.Contains(args.AllocationStack, "Microsoft.IO.RecyclableMemoryStream..ctor");
                StringAssert.Contains(args.DisposeStack, "Microsoft.IO.RecyclableMemoryStream.Dispose");
                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            Thread.Sleep(100);
            stream.Dispose();
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventStreamDoubleDisposed()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.GenerateCallStacks = true;
            bool raised = false;
            mgr.StreamDoubleDisposed += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                StringAssert.Contains(args.AllocationStack, "Microsoft.IO.RecyclableMemoryStream..ctor");
                StringAssert.Contains(args.DisposeStack1, "Microsoft.IO.RecyclableMemoryStream.Dispose");
                StringAssert.Contains(args.DisposeStack2, "Microsoft.IO.RecyclableMemoryStream.Dispose");
                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            stream.Dispose();
            stream.Dispose();
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventStreamConvertedToArray()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.GenerateCallStacks = true;
            bool raised = false;
            mgr.StreamConvertedToArray += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                StringAssert.Contains(args.Stack, "Microsoft.IO.RecyclableMemoryStream.ToArray");
                Assert.AreEqual(1, args.Length);
                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            stream.WriteByte(1);
            stream.ToArray();
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventStreamOverCapacity()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.MaximumStreamCapacity = mgr.OptionsValue.BlockSize;
            mgr.OptionsValue.GenerateCallStacks = true;
            bool raised = false;
            mgr.StreamOverCapacity += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                StringAssert.Contains(args.AllocationStack, "Microsoft.IO.RecyclableMemoryStream..ctor");
                Assert.AreEqual(mgr.OptionsValue.BlockSize * 2, args.RequestedCapacity);
                Assert.AreEqual(mgr.OptionsValue.BlockSize, args.MaximumCapacity);
                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);

            Assert.ThrowsException<OutOfMemoryException>(() => stream.Capacity = mgr.OptionsValue.BlockSize * 2);
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventBlockCreated()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            bool raised = false;
            mgr.BlockCreated += (obj, args) =>
            {
                Assert.AreEqual(mgr.OptionsValue.BlockSize, args.SmallPoolInUse);
                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventLargeBufferCreated()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            bool raised = false;
            long requestedSize = mgr.OptionsValue.LargeBufferMultiple;
            mgr.LargeBufferCreated += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.IsTrue(args.Pooled);
                Assert.AreEqual(requestedSize, args.RequiredSize);
                Assert.AreEqual(mgr.OptionsValue.LargeBufferMultiple, args.LargePoolInUse);
                Assert.IsNull(args.CallStack);

                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            byte[] buffer = this.GetRandomBuffer((int)requestedSize);
            stream.Write(buffer);
            byte[] buf2 = stream.GetBuffer();
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventUnpooledLargeBufferCreated()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.GenerateCallStacks = true;
            bool raised = false;
            long requestedSize = mgr.OptionsValue.MaximumBufferSize + 1;
            mgr.LargeBufferCreated += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.IsFalse(args.Pooled);
                Assert.IsTrue(args.RequiredSize >= requestedSize);
                Assert.IsTrue(args.LargePoolInUse >= requestedSize);
                Assert.IsNotNull(args.CallStack);

                raised = true;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);
            byte[] buffer = this.GetRandomBuffer((int)requestedSize);
            stream.Write(buffer);
            byte[] buf2 = stream.GetBuffer();
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void EventBlockDiscarded()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.MaximumSmallPoolFreeBytes = mgr.OptionsValue.BlockSize;
            int raisedCount = 0;
            long requestedSize = mgr.OptionsValue.BlockSize;
            mgr.BufferDiscarded += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.AreEqual(RecyclableMemoryStreamManager.Events.MemoryStreamBufferType.Small, args.BufferType);
                Assert.AreEqual(RecyclableMemoryStreamManager.Events.MemoryStreamDiscardReason.EnoughFree, args.Reason);

                raisedCount++;
            };
            RecyclableMemoryStream stream1 = mgr.GetStream("UnitTest", 13);
            RecyclableMemoryStream stream2 = mgr.GetStream("UnitTest", 13);
            byte[] buffer = this.GetRandomBuffer((int)requestedSize);
            stream1.Write(buffer);
            stream2.Write(buffer);
            stream1.Dispose();
            stream2.Dispose();
            Assert.AreEqual(1, raisedCount);
        }

        [TestMethod]
        public void EventLargeBufferDiscardedEnoughFree()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();
            mgr.OptionsValue.MaximumLargePoolFreeBytes = mgr.OptionsValue.LargeBufferMultiple;
            int raisedCount = 0;
            long requestedSize = mgr.OptionsValue.LargeBufferMultiple;
            mgr.BufferDiscarded += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.AreEqual(RecyclableMemoryStreamManager.Events.MemoryStreamBufferType.Large, args.BufferType);
                Assert.AreEqual(RecyclableMemoryStreamManager.Events.MemoryStreamDiscardReason.EnoughFree, args.Reason);

                raisedCount++;
            };
            RecyclableMemoryStream stream1 = mgr.GetStream("UnitTest", 13);
            RecyclableMemoryStream stream2 = mgr.GetStream("UnitTest", 13);
            byte[] buffer = this.GetRandomBuffer((int)requestedSize);
            stream1.Write(buffer);
            stream2.Write(buffer);
            stream1.GetBuffer();
            stream2.GetBuffer();
            stream1.Dispose();
            stream2.Dispose();
            Assert.AreEqual(1, raisedCount);
        }

        [TestMethod]
        public void EventLargeBufferDiscardedTooLarge()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();

            int raisedCount = 0;
            long requestedSize = mgr.OptionsValue.MaximumBufferSize + 1;
            mgr.BufferDiscarded += (obj, args) =>
            {
                Assert.AreNotEqual(Guid.Empty, args.Id);
                Assert.AreEqual("UnitTest", args.Tag);
                Assert.AreEqual(RecyclableMemoryStreamManager.Events.MemoryStreamBufferType.Large, args.BufferType);
                Assert.AreEqual(RecyclableMemoryStreamManager.Events.MemoryStreamDiscardReason.TooLarge, args.Reason);

                raisedCount++;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);

            byte[] buffer = this.GetRandomBuffer((int)requestedSize);
            stream.Write(buffer);
            stream.GetBuffer();
            stream.Dispose();

            Assert.AreEqual(1, raisedCount);
        }

        [TestMethod]
        public void EventUsageReport()
        {
            RecyclableMemoryStreamManager mgr = this.GetMemoryManager();

            int raisedCount = 0;
            long requestedSize = mgr.OptionsValue.BlockSize;
            mgr.UsageReport += (obj, args) =>
            {
                Assert.AreEqual(raisedCount == 0 ? 0 : mgr.OptionsValue.BlockSize, args.SmallPoolFreeBytes);
                Assert.AreEqual(raisedCount == 0 ? mgr.OptionsValue.BlockSize : 0, args.SmallPoolInUseBytes);
                Assert.AreEqual(0, args.LargePoolFreeBytes);
                Assert.AreEqual(0, args.LargePoolInUseBytes);

                raisedCount++;
            };
            RecyclableMemoryStream stream = mgr.GetStream("UnitTest", 13);

            byte[] buffer = this.GetRandomBuffer((int)requestedSize);
            stream.Write(buffer);
            stream.GetBuffer();
            stream.Dispose();

            Assert.AreEqual(2, raisedCount);
        }

        #endregion

        #region Test Helpers
        internal RecyclableMemoryStream GetDefaultStream()
        {
            return new RecyclableMemoryStream(this.GetMemoryManager());
        }

        protected byte[] GetRandomBuffer(int length)
        {
            byte[] buffer = new byte[length];
            this.random.NextBytes(buffer);
            return buffer;
        }

        internal virtual RecyclableMemoryStreamManager GetMemoryManager()
        {
            return new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options
            {
                BlockSize = DefaultBlockSize,
                LargeBufferMultiple = DefaultLargeBufferMultiple,
                MaximumBufferSize = DefaultMaximumBufferSize,
                UseExponentialLargeBuffer = this.UseExponentialLargeBuffer,
                AggressiveBufferReturn = this.AggressiveBufferRelease,
                ZeroOutBuffer = this.ZeroOutBuffer,
            });
        }

        private RecyclableMemoryStream GetRandomStream()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            byte[] buffer = this.GetRandomBuffer(stream.Capacity * 2);
            stream.Write(buffer, 0, buffer.Length);
            stream.Position = 0;
            return stream;
        }
        #endregion

        #region Bug Reports
        // Issue #176 - SmallPoolInUseSize, SmallPoolFreeSize
        [TestMethod]
        public void Issue176_PoolInUseSizeDoesNotDecrease()
        {
            long maximumFreeSmallPoolBytes = 32000L * 128000; //4096000000
            long maximumFreeLargePoolBytes = uint.MaxValue;
            int blockSize = 128000;

            RecyclableMemoryStreamManager mgr = new(new RecyclableMemoryStreamManager.Options
            {
                BlockSize = blockSize,
                LargeBufferMultiple = 1 << 20,
                MaximumBufferSize = 8 * (1 << 20),
                MaximumSmallPoolFreeBytes = maximumFreeSmallPoolBytes,
                MaximumLargePoolFreeBytes = maximumFreeLargePoolBytes
            });

            RecyclableMemoryStream fillStream = mgr.GetStream("pool", requiredSize: 128000, asContiguousBuffer: true);
            byte[] block1 = new byte[128000];
            long test1 = 4096000000;
            int test2 = 128000;
            for (int i = 0; i < 32000; i++)
            {
                fillStream.Write(block1, 0, 128000);
                test1 -= test2;
            }

            Assert.AreEqual(0, test1);
            Assert.AreEqual(maximumFreeSmallPoolBytes, mgr.SmallPoolInUseSize);
            fillStream.Dispose();
            Assert.AreEqual(0, mgr.SmallPoolInUseSize);
        }
        #endregion

        #region ZeroOutBuffer

        [TestMethod]
        public void BlockZeroedBeforeReturn()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            memMgr.ReturnBlock(this.GetRandomBuffer(memMgr.OptionsValue.BlockSize), DefaultId, DefaultTag);
            Assert.AreEqual(1, memMgr.SmallBlocksFree);
            byte[] block = memMgr.GetBlock();
            if (this.ZeroOutBuffer)
            {
                CollectionAssert.AreEqual(new byte[block.Length], block);
            }
            else
            {
                CollectionAssert.AreNotEqual(new byte[block.Length], block);
            }
        }

        [TestMethod]
        public void BlocksZeroedBeforeReturn()
        {
            const int numBlocks = 5;
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            List<byte[]> blocks = new(numBlocks);
            for (int blockId = 0; blockId < numBlocks; ++blockId)
            {
                blocks.Add(this.GetRandomBuffer(memMgr.OptionsValue.BlockSize));
            }
            memMgr.ReturnBlocks(blocks, DefaultId, DefaultTag);
            Assert.AreEqual(blocks.Count, memMgr.SmallBlocksFree);
            for (int blockId = 0; blockId < blocks.Count; ++blockId)
            {
                byte[] block = memMgr.GetBlock();
                if (this.ZeroOutBuffer)
                {
                    CollectionAssert.AreEqual(new byte[block.Length], block);
                }
                else
                {
                    CollectionAssert.AreNotEqual(new byte[block.Length], block);
                }
            }
        }

        [TestMethod]
        public void LargeBufferZeroedBeforeReturn()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            memMgr.ReturnLargeBuffer(this.GetRandomBuffer(memMgr.OptionsValue.LargeBufferMultiple), DefaultId, DefaultTag);
            Assert.AreEqual(1, memMgr.LargeBuffersFree);
            byte[] buffer = memMgr.GetLargeBuffer(memMgr.OptionsValue.LargeBufferMultiple, DefaultId, DefaultTag);
            if (this.ZeroOutBuffer)
            {
                CollectionAssert.AreEqual(new byte[buffer.Length], buffer);
            }
            else
            {
                CollectionAssert.AreNotEqual(new byte[buffer.Length], buffer);
            }
        }

        #endregion

        protected abstract bool AggressiveBufferRelease { get; }
        protected virtual bool ZeroOutBuffer => false;

        protected virtual bool UseExponentialLargeBuffer => false;

        protected static class RMSAssert
        {
            /// <summary>
            /// Asserts that two array segments are equal
            /// </summary>
            internal static void BuffersAreEqual(ArraySegment<byte> seg1, ArraySegment<byte> seg2)
            {
                Assert.AreEqual(seg1.Count, seg2.Count);
                BuffersAreEqual(seg1, seg2, seg1.Count);
            }

            /// <summary>
            /// Asserts that two buffers are equal, up to the given count
            /// </summary>
            internal static void BuffersAreEqual(ReadOnlySpan<byte> buffer1, ReadOnlySpan<byte> buffer2, int count)
            {
                BuffersAreEqual(buffer1, 0, buffer2, 0, count);
            }

            /// <summary>
            /// Asserts that two buffers are equal, up to the given count, starting at the specific offsets for each buffer
            /// </summary>
            internal static void BuffersAreEqual(ReadOnlySpan<byte> buffer1, int offset1, ReadOnlySpan<byte> buffer2, int offset2, int count,
                                                 double tolerance = 0.0)
            {
                if (buffer1 == null && buffer2 == null)
                {
                    //If both null, it's OK
                    return;
                }

                // If either one is null, we fail
                Assert.IsTrue(buffer1 != null && buffer2 != null);

                Assert.IsTrue(buffer1.Length - offset1 >= count);

                Assert.IsTrue(buffer2.Length - offset2 >= count);

                int errors = 0;
                for (int i1 = offset1, i2 = offset2; i1 < offset1 + count; ++i1, ++i2)
                {
                    if (tolerance == 0.0)
                    {
                        if (buffer1[i1] != buffer2[i2])
                        {
                            Assert.Fail(string.Format("Buffers are different. buffer1[{0}]=={1}, buffer2[{2}]=={3}", i1,
                                                  buffer1[i1], i2, buffer2[i2]));
                        }
                    }
                    else
                    {
                        if (buffer1[i1] != buffer2[i2])
                        {
                            errors++;
                        }
                    }
                }
                double rate = (double)errors / count;
                Assert.IsTrue(rate <= tolerance, $"Too many errors. Buffers can differ to a tolerance of {tolerance:F4}");
            }

            internal static void TryGetBufferEqualToGetBuffer(RecyclableMemoryStream stream)
            {
                byte[] buffer = stream.GetBuffer();

                Assert.IsTrue(stream.TryGetBuffer(out ArraySegment<byte> segment));
                Assert.AreEqual(0, segment.Offset);
                Assert.AreEqual(stream.Length, segment.Count);
                BuffersAreEqual(segment.Array, buffer, buffer.Length);
            }

            /// <summary>
            /// Ensures that stream contains multiple exact copies of the passed buffer
            /// </summary>
            internal static void StreamContainsExactCopies(RecyclableMemoryStream stream, ReadOnlySpan<byte> buffer)
            {
                byte[] temp = new byte[buffer.Length];
                stream.Position = 0;
                while (stream.Read(temp) > 0)
                {
                    BuffersAreEqual(buffer, temp, temp.Length);
                }
            }
        }
    }

    [TestClass]
    public sealed class RecyclableMemoryStreamTestsWithPassiveBufferRelease : BaseRecyclableMemoryStreamTests
    {
        protected override bool AggressiveBufferRelease => false;

        [TestMethod]
        public void OldBuffersAreKeptInStreamUntilDispose()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            stream.GetBuffer();

            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * 1, memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolInUseSize);

            stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (1 + 2), memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolInUseSize);

            stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (1 + 2 + 3), memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolInUseSize);

            stream.Dispose();

            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (1 + 2 + 3), memMgr.LargePoolFreeSize);
            Assert.AreEqual(0, memMgr.LargePoolInUseSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }
    }

    [TestClass]
    public sealed class RecyclableMemoryStreamTestsWithAggressiveBufferRelease : BaseRecyclableMemoryStreamTests
    {
        protected override bool AggressiveBufferRelease => true;
    }

    public abstract class BaseRecyclableMemoryStreamTestsUsingExponentialLargeBuffer : BaseRecyclableMemoryStreamTests
    {
        protected override bool UseExponentialLargeBuffer => true;

        [TestMethod]
        public override void RecyclableMemoryManagerUsingMultipleOrExponentialLargeBuffer()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            Assert.IsTrue(memMgr.OptionsValue.UseExponentialLargeBuffer);
        }

        [TestMethod]
        public override void RecyclableMemoryManagerThrowsExceptionOnMaximumBufferNotMultipleOrExponentialOfLargeBufferMultiple()
        {
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 2025, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 2023, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            Assert.ThrowsException<InvalidOperationException>(() => new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 3072, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer }));
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 2048, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer });
            _ = new RecyclableMemoryStreamManager(new RecyclableMemoryStreamManager.Options { BlockSize = 100, LargeBufferMultiple = 1024, MaximumBufferSize = 4096, UseExponentialLargeBuffer = this.UseExponentialLargeBuffer });
        }

        [TestMethod]
        public override void GetLargeBufferAlwaysAMultipleOrExponentialOfMegabyteAndAtLeastAsMuchAsRequestedForLargeBuffer()
        {
            const int step = 200000;
            const int start = 1;
            const int end = 16000000;
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();

            for (int i = start; i <= end; i += step)
            {
                byte[] buffer = memMgr.GetLargeBuffer(i, DefaultId, DefaultTag);
                Assert.IsTrue(buffer.Length >= i);
                Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (int)Math.Pow(2, Math.Floor(Math.Log(buffer.Length / memMgr.OptionsValue.LargeBufferMultiple, 2))), buffer.Length, $"buffer length of {buffer.Length} is not a exponential of {memMgr.OptionsValue.LargeBufferMultiple}");
            }
        }

        [TestMethod]
        public override void AllMultiplesOrExponentialUpToMaxCanBePooled()
        {
            const int BlockSize = 100;
            const int LargeBufferMultiple = 1000;
            const int MaxBufferSize = 8000;

            for (int size = LargeBufferMultiple; size <= MaxBufferSize; size *= 2)
            {
                RecyclableMemoryStreamManager memMgr = new(
                    new RecyclableMemoryStreamManager.Options
                    {
                        BlockSize = BlockSize,
                        LargeBufferMultiple = LargeBufferMultiple,
                        MaximumBufferSize = MaxBufferSize,
                        UseExponentialLargeBuffer = this.UseExponentialLargeBuffer,
                        AggressiveBufferReturn = this.AggressiveBufferRelease
                    });

                byte[] buffer = memMgr.GetLargeBuffer(size, DefaultId, DefaultTag);
                Assert.AreEqual(0, memMgr.LargePoolFreeSize);
                Assert.AreEqual(size, memMgr.LargePoolInUseSize);

                memMgr.ReturnLargeBuffer(buffer, DefaultId, DefaultTag);

                Assert.AreEqual(size, memMgr.LargePoolFreeSize);
                Assert.AreEqual(0, memMgr.LargePoolInUseSize);
            }
        }

        [TestMethod]
        public override void RequestTooLargeBufferAdjustsInUseCounter()
        {
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            byte[] buffer = memMgr.GetLargeBuffer(memMgr.OptionsValue.MaximumBufferSize + 1, DefaultId, DefaultTag);
            Assert.AreEqual(memMgr.OptionsValue.MaximumBufferSize * 2, buffer.Length);
            Assert.AreEqual(buffer.Length, memMgr.LargePoolInUseSize);
        }

        protected override void TestDroppingLargeBuffer(long maxFreeLargeBufferSize)
        {
            const int BlockSize = 100;
            const int LargeBufferMultiple = 1000;
            const int MaxBufferSize = 8000;

            for (int size = LargeBufferMultiple; size <= MaxBufferSize; size *= 2)
            {
                RecyclableMemoryStreamManager memMgr = new(new RecyclableMemoryStreamManager.Options
                {
                    BlockSize = BlockSize,
                    LargeBufferMultiple = LargeBufferMultiple,
                    MaximumBufferSize = MaxBufferSize,
                    UseExponentialLargeBuffer = this.UseExponentialLargeBuffer,
                    AggressiveBufferReturn = this.AggressiveBufferRelease,
                    MaximumLargePoolFreeBytes = maxFreeLargeBufferSize
                });

                List<byte[]> buffers = new();

                //Get one extra buffer
                long buffersToRetrieve = maxFreeLargeBufferSize > 0 ? (maxFreeLargeBufferSize / size) + 1 : 10;
                for (int i = 0; i < buffersToRetrieve; i++)
                {
                    buffers.Add(memMgr.GetLargeBuffer(size, DefaultId, DefaultTag));
                }
                Assert.AreEqual(size * buffersToRetrieve, memMgr.LargePoolInUseSize);
                Assert.AreEqual(0, memMgr.LargePoolFreeSize);
                foreach (byte[] buffer in buffers)
                {
                    memMgr.ReturnLargeBuffer(buffer, DefaultId, DefaultTag);
                }
                Assert.AreEqual(0, memMgr.LargePoolInUseSize);
                if (maxFreeLargeBufferSize > 0)
                {
                    Assert.IsTrue(memMgr.LargePoolFreeSize <= maxFreeLargeBufferSize);
                }
                else
                {
                    Assert.AreEqual(buffersToRetrieve * size, memMgr.LargePoolFreeSize);
                }
            }
        }

        [TestMethod]
        [Timeout(10000)]
        [DoNotParallelize]
        public void TryGetBuffer_InfiniteLoop_Issue344()
        {
            // see https://github.com/microsoft/Microsoft.IO.RecyclableMemoryStream/issues/344
            RecyclableMemoryStreamManager memMgr = this.GetMemoryManager();
            int size = 1073741825; // this will cause infinite loop in TryGetBuffer below, 1073741824 works.
            byte[] bytes = new byte[size];
            using (RecyclableMemoryStream ms = memMgr.GetStream())
            {
                ms.Write(bytes, 0, size);
                bool result = ms.TryGetBuffer(out ArraySegment<byte> segment);
                Assert.IsFalse(result);
                Assert.AreEqual(0, segment.Count);
            }
        }
    }

    [TestClass]
    public sealed class RecyclableMemoryStreamTestsWithPassiveBufferReleaseUsingExponentialLargeBuffer : BaseRecyclableMemoryStreamTestsUsingExponentialLargeBuffer
    {
        protected override bool AggressiveBufferRelease => false;

        [TestMethod]
        public void OldBuffersAreKeptInStreamUntilDispose()
        {
            RecyclableMemoryStream stream = this.GetDefaultStream();
            RecyclableMemoryStreamManager memMgr = stream.MemoryManager;
            byte[] buffer = this.GetRandomBuffer(stream.MemoryManager.OptionsValue.LargeBufferMultiple);
            stream.Write(buffer, 0, buffer.Length);
            stream.GetBuffer();

            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * 1, memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolInUseSize);

            stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (1 + 2), memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolInUseSize);

            stream.Write(buffer, 0, buffer.Length);

            Assert.AreEqual(0, memMgr.LargePoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (1 + 2 + 4), memMgr.LargePoolInUseSize);
            Assert.AreEqual(0, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolInUseSize);

            stream.Dispose();

            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple * (1 + 2 + 4), memMgr.LargePoolFreeSize);
            Assert.AreEqual(0, memMgr.LargePoolInUseSize);
            Assert.AreEqual(memMgr.OptionsValue.LargeBufferMultiple, memMgr.SmallPoolFreeSize);
            Assert.AreEqual(0, memMgr.SmallPoolInUseSize);
        }
    }

    [TestClass]
    public sealed class RecyclableMemoryStreamTestsWithAggressiveBufferReleaseUsingExponentialLargeBuffer : BaseRecyclableMemoryStreamTestsUsingExponentialLargeBuffer
    {
        protected override bool AggressiveBufferRelease => true;
    }

    [TestClass]
    [DoNotParallelize]
    public sealed class RecyclableMemoryStreamTestsWithZeroOutBuffer : BaseRecyclableMemoryStreamTests
    {
        protected override bool AggressiveBufferRelease => false;

        protected override bool ZeroOutBuffer => true;
    }
}
