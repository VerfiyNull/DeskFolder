/*
    DeskFolder
    
    This program is free software: you can redistribute it and/or modify
    it under the terms of the DeskFolder Custom License.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using Avalonia;
using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace DeskFolder;

class Program
{
    private const string MutexName = "DeskFolder_Global_Mutex";
    private const string PipeName = "DeskFolder_Pipe";
    
    // Event to notify the running app of new arguments
    public static event Action<string[]>? ArgumentsReceived;

    [STAThread]
    public static void Main(string[] args)
    {
        // 1. Check Mutex
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        bool isAlreadyRunning = !createdNew;

        // 2. Check Process Name (as explicit safety check)
        if (!isAlreadyRunning)
        {
            try 
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                var processes = System.Diagnostics.Process.GetProcessesByName(current.ProcessName);
                // If there's more than 1 (us), then another one is running
                if (processes.Length > 1) 
                {
                    // Check if the other process is not us (by ID)
                    foreach (var p in processes)
                    {
                        if (p.Id != current.Id) 
                        {
                            isAlreadyRunning = true; 
                            break;
                        }
                    }
                }
            }
            catch {}
        }

        if (!isAlreadyRunning)
        {
            // Start server to listen for subsequent instances
            Task.Run(StartServer);
            
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        else
        {
            // Send args to the existing instance
            SendArgs(args);
        }
    }

    private static async Task StartServer()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                await server.WaitForConnectionAsync();
                
                using var reader = new StreamReader(server);
                var argsLine = await reader.ReadToEndAsync();
                
                // Invoke even if empty (triggers "Bring to Front")
                var args = string.IsNullOrEmpty(argsLine) 
                    ? Array.Empty<string>() 
                    : argsLine.Split('|');
                    
                ArgumentsReceived?.Invoke(args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pipe Server Error: {ex.Message}");
                await Task.Delay(1000); 
            }
        }
    }

    private static void SendArgs(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000); // 1s timeout
            
            using var writer = new StreamWriter(client);
            writer.Write(string.Join("|", args));
            writer.Flush();
        }
        catch
        {
            // Fail silently
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
