using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace BlackScreensWPF
{
    /// <summary>
    /// One BlackScreens per user session. A second launch hands its command line to the running
    /// instance over a named pipe and exits (e.g. launching it again from the Start menu opens settings).
    /// </summary>
    public sealed class SingleInstance : IDisposable
    {
        private static readonly string InstanceId = "BlackScreens-" + WindowsIdentity.GetCurrent().User?.Value + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
        private static readonly string MutexName = "Local\\" + InstanceId;
        private static readonly string PipeName = InstanceId;

        private readonly Mutex mutex;
        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        public bool IsFirstInstance { get; }

        public SingleInstance()
        {
            mutex = new Mutex(true, MutexName, out bool createdNew);
            IsFirstInstance = createdNew;
        }

        /// <summary>
        /// Send the command line to the running instance. Returns false if it could not be reached.
        /// </summary>
        public static bool SendToRunningInstance(string[] args)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000);
                    using (StreamWriter writer = new StreamWriter(client))
                    {
                        // An empty line means "no arguments", treated by the receiver as "show settings"
                        writer.WriteLine(String.Join("\t", args));
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                CommonData.dataInstance.LogToFile.Warn(ex, "SendToRunningInstance() failed");
                return false;
            }
        }

        /// <summary>
        /// Listen for command lines sent by later launches. onCommand is called on a background thread.
        /// </summary>
        public void StartListening(Action<string[]> onCommand)
        {
            Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        // CurrentUserOnly: other users on this machine can't drive this instance
                        using (NamedPipeServerStream server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
                        {
                            await server.WaitForConnectionAsync(cts.Token);
                            using (StreamReader reader = new StreamReader(server))
                            {
                                string line = await reader.ReadLineAsync(cts.Token) ?? "";
                                string[] args = line.Length == 0 ? Array.Empty<string>() : line.Split('\t');
                                onCommand(args);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        CommonData.dataInstance.LogToFile.Warn(ex, "SingleInstance pipe listener error");
                        await Task.Delay(500);
                    }
                }
            });
        }

        public void Dispose()
        {
            cts.Cancel();
            if (IsFirstInstance)
            {
                try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            }
            mutex.Dispose();
        }
    }
}
