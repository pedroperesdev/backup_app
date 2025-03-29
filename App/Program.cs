using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text;
using System.Reflection.Metadata;

/*
    Program that executes a one-way synchronization from source(original)
    folder to a replica folder. Once running, enter the following
    <sourceDir>;<replicaDir>;<intervalInSeconds>;<logFilePath>
    separated by ";".
    Since the program synchronizes two folders, both <sourceDir>
    and <replicaDir> need to exist.
    The synchronization detects file/folder additions, removals 
    and updates.
    Type QUIT to exit program.

*/


public class Program{

    //Lock prevents cropped text in log file due to multiple threads writing to file
    private static readonly object logFileLock = new object();
    // Controls the main loop
    static bool running = true; 
    static void Main()
    {
        string input = string.Empty, original = string.Empty, replica = string.Empty, log = string.Empty;
        int interval = 0;
        string[] line = Array.Empty<string>();
        bool validInput = false;
        
        //Verification of the parameters
        while(!validInput){

            input = ParseArgs();
            try{
                line = input.Split(';');
                if(line.Length == 4){
                    original = line[0];
                    replica = line[1];
                    if (!int.TryParse(line[2], out interval))
                    {
                        throw new ArgumentException("Interval is not a valid integer.");
                    }
                    log = line[3];
                }
                else{
                    throw new ArgumentException("Invalid input format");
                }
                
            } catch (Exception ex) {Console.WriteLine($"Error: {ex.Message}");}

            try{

                //Specifies which parameter is not correct
                if(!Directory.Exists(original))
                    throw new DirectoryNotFoundException("Original directory does not exist.");
                else if(!Directory.Exists(replica))
                    throw new DirectoryNotFoundException("Replica directory does not exist.");
                else if(!Directory.Exists(Path.GetDirectoryName(log)))
                    throw new DirectoryNotFoundException("Log directory does not exist.");
                else{
                    validInput = true;
                }
            } catch(Exception ex) {Console.WriteLine($"Error: {ex.Message}");}
        }

        //Asynchronously waits for QUIT command
        Task.Run(() =>
        {
            while (true)
            {
                string input = Console.ReadLine()?.Trim() ?? string.Empty;
                if (input?.ToUpper() == "QUIT")
                {
                    Console.WriteLine("[BACKUP] - Stopping backup process...");
                    running = false;
                    break;
                }
            }
        });
                
        //Main backup loop
        while(running){

            Stopwatch stopwatch = new Stopwatch();

            string logMessage = $"\n[BACKUP] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Starting backup..." + Environment.NewLine;
            File.AppendAllText(log, logMessage);
            Console.WriteLine(logMessage);

            stopwatch.Start();
            Backup(original, replica, log);
            stopwatch.Stop();

            string logMessageEnd = $"\n[BACKUP] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Backup completed successfully. Time taken: {stopwatch.ElapsedMilliseconds} ms." + Environment.NewLine;
            File.AppendAllText(log, logMessageEnd);
            Console.WriteLine(logMessageEnd);

            Thread.Sleep(interval*1000);
        }
    }

    static string ParseArgs(){
        string input = string.Empty;
        Console.WriteLine("Enter original directory, replica directory, synchronization interval (in seconds) and log file path separated by space:");
        input = Console.ReadLine() ?? string.Empty;
        return input;

    }

    //Every operation in Backup function uses Parallel.ForEach() for multiple threads
    static void Backup(string originalDir, string replicaDir, string logDir)
    {
        int smallFileSizeThreshold = 8 * 1024;
        int mediumFileSizeThreshold = 10 * 1024 * 1024;
        int smallBuffer = 8 * 1024;
        int mediumBuffer = 64 * 1024;
        int largeBuffer = 256 * 1024;

        string[] files = Directory.GetFiles(originalDir);
        string[] directories = Directory.GetDirectories(originalDir);

        string[] replicaFiles = Directory.GetFiles(replicaDir);
        string[] replicaDirs = Directory.GetDirectories(replicaDir);
            

        Parallel.ForEach(files, originalFilePath =>
        {
            string fileName = Path.GetFileName(originalFilePath);
            string replicaFilePath = Path.Combine(replicaDir, fileName);

            //Try-catch block because user might be changing files in source during backup, causing conflicts
            try{
                if (!File.Exists(replicaFilePath))
                {
                    long fileSize = new FileInfo(originalFilePath).Length;

                    if (fileSize <= smallFileSizeThreshold)
                    {
                        CopyFileWithBuffer(originalFilePath, replicaFilePath, smallBuffer);
                    }

                    else if (fileSize <= mediumFileSizeThreshold)
                    {
                        CopyFileWithBuffer(originalFilePath, replicaFilePath, mediumBuffer);
                    }
                    
                    else
                    {
                        CopyFileWithBuffer(originalFilePath, replicaFilePath, largeBuffer);
                    }
                    string logMessage = $"(+) Copied {fileName} to replica folder." + Environment.NewLine;
                    Console.WriteLine(logMessage);
                    lock (logFileLock)
                    {
                        File.AppendAllText(logDir,logMessage);
                    }
                }
                else
                {
                    
                    //Comparing all file hashes takes too long, so we compare
                    //modified times. If modified, then we check hashes.

                    DateTime originalModified = File.GetLastWriteTime(originalFilePath);
                    DateTime replicaModified = File.GetLastWriteTime(replicaFilePath);

                    //If source file has a more recent modified time, we need to update it 
                    if (originalModified > replicaModified){

                        long fileSize = new FileInfo(originalFilePath).Length;
                        string originalFileHash;
                        string replicaFileHash;

                        if (fileSize <= smallFileSizeThreshold)
                        {
                            originalFileHash = GetFileMD5(originalFilePath, smallBuffer);
                            replicaFileHash = GetFileMD5(replicaFilePath, smallBuffer);
                        }

                        else if (fileSize <= mediumFileSizeThreshold)
                        {
                            originalFileHash = GetFileMD5(originalFilePath, mediumBuffer);
                            replicaFileHash = GetFileMD5(replicaFilePath, mediumBuffer);
                        }

                        else 
                        {
                            originalFileHash = GetFileMD5(originalFilePath, largeBuffer);
                            replicaFileHash = GetFileMD5(replicaFilePath, largeBuffer);
                        }

                        if (originalFileHash != replicaFileHash)
                        {   
                            // Overwrites the file
                            File.Copy(originalFilePath, replicaFilePath, true); 
                            string logMessage = $"(~) Updated {fileName} to a newer version." + Environment.NewLine;
                            Console.WriteLine(logMessage);
                            lock (logFileLock)
                            {
                                File.AppendAllText(logDir,logMessage);
                            }
                        }
                    }
                }
            }

            catch (FileNotFoundException)
            {
                Console.WriteLine($"Error: The file '{fileName}' was deleted during the backup process.");
            }
            
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"Error: Access to the file '{fileName}' was denied.");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error while copying '{fileName}': {ex.Message}");
            }
        });

        // Delete extra files in the replica that no longer exist in the original
        Parallel.ForEach (replicaFiles, replicaFilePath =>
        {
            string fileName = Path.GetFileName(replicaFilePath);
            string originalFilePath = Path.Combine(originalDir, fileName);

            if (!File.Exists(originalFilePath) && (fileName != Path.GetFileName(logDir)))
            {
                File.Delete(replicaFilePath);
                string logMessage = $"(-) Deleted {fileName} from replica." + Environment.NewLine;
                Console.WriteLine(logMessage);
                lock (logFileLock)
                {
                    File.AppendAllText(logDir,logMessage);
                }
            }
        });

        Parallel.ForEach (directories, subDirectory =>
        {
            // Call the same method on each directory.
            string newReplicaFolder = Path.GetFileName(subDirectory);
    
            string newReplicaDir = Path.Combine(replicaDir, newReplicaFolder);

            // Create the directory if it doesn't exist
            Directory.CreateDirectory(newReplicaDir);
            Backup(subDirectory, newReplicaDir, logDir);
        });

        // Delete extra directories in the replica that no longer exist in the original
        Parallel.ForEach (replicaDirs, replicaSubDir =>
        {
            string dirName = Path.GetFileName(replicaSubDir);
            string originalSubDir = Path.Combine(originalDir, dirName);

            if (!Directory.Exists(originalSubDir))
            {
                Directory.Delete(replicaSubDir, true);
                string logMessage = $"(-) Deleted folder {dirName} from replica." + Environment.NewLine;
                Console.WriteLine(logMessage);
                lock (logFileLock)
                {
                    File.AppendAllText(logDir,logMessage);
                }
            }
        });
    }

    static void CopyFileWithBuffer(string sourcePath, string destPath, int bufferSize)
    {

        using (FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read))
        using (FileStream destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write))
        using (BufferedStream bufferedSourceStream = new BufferedStream(sourceStream, bufferSize))
        using (BufferedStream bufferedDestStream = new BufferedStream(destStream, bufferSize))
        {
            bufferedSourceStream.CopyTo(bufferedDestStream);
        }
    }

    // Method for hashing files with adjustable buffer size
    static string GetFileMD5(string filePath, int bufferSize)
    {
        using (MD5 md5 = MD5.Create())
        using (FileStream fileStream = File.OpenRead(filePath))
        {
            byte[] buffer = new byte[bufferSize];
            int bytesRead;

            while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            md5.TransformFinalBlock(buffer, 0, 0);

            StringBuilder sb = new StringBuilder();
            if(md5.Hash != null){
                foreach (byte b in md5.Hash)
                {
                    sb.Append(b.ToString("x2"));
                }
            }
            return sb.ToString();
        }
    }

}
