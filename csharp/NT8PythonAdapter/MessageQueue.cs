using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Threading;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Thread-safe message queue for outbound messages to Python
    /// Prevents blocking on Named Pipe writes
    /// </summary>
    public class MessageQueue
    {
        private readonly ConcurrentQueue<byte[]> messageQueue;
        private readonly NamedPipeServerStream pipeStream;
        private readonly Action<string> logCallback;

        private Thread senderThread;
        private bool isRunning;
        private readonly object sendLock = new object();

        // Statistics
        private long messagesSent;
        private long messagesQueued;
        private long sendErrors;

        public MessageQueue(NamedPipeServerStream pipeStream, Action<string> logCallback)
        {
            this.pipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
            this.logCallback = logCallback;

            messageQueue = new ConcurrentQueue<byte[]>();
        }

        #region Queue Management

        /// <summary>
        /// Start the sender thread
        /// </summary>
        public void Start()
        {
            if (isRunning)
                return;

            isRunning = true;
            senderThread = new Thread(SenderLoop)
            {
                IsBackground = true,
                Name = "NT8Python-MessageSender"
            };
            senderThread.Start();

            logCallback?.Invoke("Message queue started");
        }

        /// <summary>
        /// Stop the sender thread
        /// </summary>
        public void Stop()
        {
            isRunning = false;

            // Wait for thread to finish
            if (senderThread != null && senderThread.IsAlive)
            {
                senderThread.Join(2000);
            }

            logCallback?.Invoke("Message queue stopped");
        }

        /// <summary>
        /// Enqueue a message to send
        /// </summary>
        public bool Enqueue(byte[] message)
        {
            if (message == null || message.Length == 0)
                return false;

            try
            {
                messageQueue.Enqueue(message);
                messagesQueued++;
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"ERROR enqueueing message: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Sender Thread

        /// <summary>
        /// Main sender loop - runs on background thread
        /// </summary>
        private void SenderLoop()
        {
            while (isRunning)
            {
                try
                {
                    // Check if there are messages to send
                    if (messageQueue.TryDequeue(out byte[] message))
                    {
                        SendMessage(message);
                    }
                    else
                    {
                        // No messages, sleep briefly
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"ERROR in sender loop: {ex.Message}");
                    Thread.Sleep(10);
                }
            }

            // Drain remaining messages
            DrainQueue();
        }

        /// <summary>
        /// Send a single message to the pipe
        /// </summary>
        private void SendMessage(byte[] message)
        {
            try
            {
                lock (sendLock)
                {
                    if (pipeStream != null && pipeStream.IsConnected)
                    {
                        pipeStream.Write(message, 0, message.Length);
                        pipeStream.Flush();
                        messagesSent++;
                    }
                }
            }
            catch (Exception ex)
            {
                sendErrors++;
                logCallback?.Invoke($"ERROR sending message: {ex.Message}");
            }
        }

        /// <summary>
        /// Drain remaining messages on shutdown
        /// </summary>
        private void DrainQueue()
        {
            int drained = 0;
            while (messageQueue.TryDequeue(out byte[] message) && drained < 100)
            {
                SendMessage(message);
                drained++;
            }

            if (drained > 0)
            {
                logCallback?.Invoke($"Drained {drained} messages from queue");
            }
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Get queue statistics
        /// </summary>
        public QueueStats GetStats()
        {
            return new QueueStats
            {
                MessagesQueued = messagesQueued,
                MessagesSent = messagesSent,
                SendErrors = sendErrors,
                QueueSize = messageQueue.Count,
                IsRunning = isRunning
            };
        }

        /// <summary>
        /// Get queue depth
        /// </summary>
        public int GetQueueDepth()
        {
            return messageQueue.Count;
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Stop();
            while (messageQueue.TryDequeue(out _)) { }
        }

        #endregion
    }

    /// <summary>
    /// Message queue statistics
    /// </summary>
    public class QueueStats
    {
        public long MessagesQueued { get; set; }
        public long MessagesSent { get; set; }
        public long SendErrors { get; set; }
        public int QueueSize { get; set; }
        public bool IsRunning { get; set; }

        public override string ToString()
        {
            return $"Queue: {QueueSize} pending, {MessagesSent} sent, {SendErrors} errors";
        }
    }
}
