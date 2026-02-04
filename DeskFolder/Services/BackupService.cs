using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DeskFolder.Services
{
    public class BackupItem
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTime CreationTime { get; set; }
        public string SizeDisplay { get; set; } = "";
    }

    public class BackupService
    {
        private readonly string _dataFolder;
        private readonly string _backupFolder;

        public BackupService()
        {
            _dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskFolder");
            _backupFolder = Path.Combine(_dataFolder, "Backups");
            
            if (!Directory.Exists(_backupFolder))
            {
                Directory.CreateDirectory(_backupFolder);
            }
        }

        public async Task<string> CreateBackupAsync(IProgress<string>? progress = null)
        {
            // Timeout 45s
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            
            return await Task.Run(() =>
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupPath = Path.Combine(_backupFolder, $"Backup_{timestamp}.zip");

                try
                {
                    progress?.Report("Initializing...");
                    if (!Directory.Exists(_backupFolder)) Directory.CreateDirectory(_backupFolder);

                    using (var fs = new FileStream(backupPath, FileMode.Create))
                    using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                    {
                        // 1. Snapshot Config immediately (in memory)
                        progress?.Report("Backing up settings...");
                        string configPath = Path.Combine(_dataFolder, "config.json");
                        if (File.Exists(configPath))
                        {
                            try 
                            {
                                var entry = archive.CreateEntry("config.json");
                                using (var entryStream = entry.Open())
                                using (var configStream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                {
                                    configStream.CopyTo(entryStream);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error backing up config: {ex}");
                            }
                        }

                        // 2. Stream Folders directly to Zip
                        string foldersSrcDir = Path.Combine(_dataFolder, "Folders");
                        
                        // Ensure the folder structure exists in zip even if empty
                        var rootEntry = archive.CreateEntry("Folders/");
                        
                        if (Directory.Exists(foldersSrcDir))
                        {
                            // Parse config to get only valid Folder IDs
                            // This ignores orphaned data from previous installs/tests
                            var validFolderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            if (File.Exists(configPath))
                            {
                                try
                                {
                                    string jsonString = File.ReadAllText(configPath);
                                    using (JsonDocument doc = JsonDocument.Parse(jsonString))
                                    {
                                        if (doc.RootElement.TryGetProperty("Folders", out JsonElement foldersElement) && 
                                            foldersElement.ValueKind == JsonValueKind.Array)
                                        {
                                            foreach (JsonElement folder in foldersElement.EnumerateArray())
                                            {
                                                if (folder.TryGetProperty("Id", out JsonElement idElement))
                                                {
                                                    string? id = idElement.GetString();
                                                    if (!string.IsNullOrEmpty(id))
                                                    {
                                                        validFolderIds.Add(id);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error parsing config for IDs: {ex}");
                                }
                            }

                            // Collect files only from valid folders
                            List<string> filesToBackup = new List<string>();
                            
                            foreach (var id in validFolderIds)
                            {
                                string folderPath = Path.Combine(foldersSrcDir, id);
                                if (Directory.Exists(folderPath))
                                {
                                    filesToBackup.AddRange(Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories));
                                }
                            }

                            int totalFiles = filesToBackup.Count;
                            int processed = 0;

                            foreach (var filePath in filesToBackup)
                            {
                                cts.Token.ThrowIfCancellationRequested();
                                processed++;
                                
                                // Update UI periodically
                                if (processed % 5 == 0 || processed == 1 || processed == totalFiles)
                                    progress?.Report($"Backing up files ({processed}/{totalFiles})...");

                                try
                                {
                                    // Make relative path for zip entry
                                    // e.g. C:\...\Folders\UUID\file.txt -> Folders/UUID/file.txt
                                    var relativePath = Path.GetRelativePath(_dataFolder, filePath).Replace('\\', '/');
                                    var entry = archive.CreateEntry(relativePath);

                                    using (var entryStream = entry.Open())
                                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                    {
                                        fileStream.CopyTo(entryStream);
                                    }
                                }
                                catch 
                                {
                                    // Skip locked/inaccessible files without failing entire backup
                                    System.Diagnostics.Debug.WriteLine($"Skipped locked file: {filePath}");
                                }
                            }
                        }
                    }

                    progress?.Report("Finalizing backup...");
                    return backupPath;
                }
                catch (OperationCanceledException)
                {
                    // Clean up partial file
                    try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                    throw new Exception("Backup timed out.");
                }
                catch (Exception)
                {
                    try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
                    throw;
                }
            }, cts.Token);
        }

        /* Removed unused CopyDirectoryWithProgress helper */

        public List<BackupItem> GetBackups()
        {
            if (!Directory.Exists(_backupFolder)) return new List<BackupItem>();

            var di = new DirectoryInfo(_backupFolder);
            var files = di.GetFiles("*.zip")
                          .OrderByDescending(f => f.CreationTime)
                          .ToList();

            return files.Select(f => new BackupItem
            {
                FilePath = f.FullName,
                FileName = f.Name,
                CreationTime = f.CreationTime,
                SizeDisplay = FormatSize(f.Length)
            }).ToList();
        }

        public async Task RestoreBackupAsync(string backupPath, IProgress<string>? progress = null)
        {
            if (!File.Exists(backupPath)) throw new FileNotFoundException("Backup file not found.");

            await Task.Run(() =>
            {
                progress?.Report("Preparing restore...");
                
                // 1. Unzip to temp
                string tempDir = Path.Combine(Path.GetTempPath(), $"DeskFolderRestore_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    progress?.Report("Extracting backup...");
                    ZipFile.ExtractToDirectory(backupPath, tempDir);

                    // 2. Restore Config
                    progress?.Report("Restoring settings...");
                    string tempConfig = Path.Combine(tempDir, "config.json");
                    if (File.Exists(tempConfig))
                    {
                        File.Copy(tempConfig, Path.Combine(_dataFolder, "config.json"), true);
                    }

                    // 3. Restore Folders
                    string tempFolders = Path.Combine(tempDir, "Folders");
                    if (Directory.Exists(tempFolders))
                    {
                        string targetFolders = Path.Combine(_dataFolder, "Folders");
                        
                        // Clear existing folders to avoid mixing
                        if (Directory.Exists(targetFolders))
                        {
                            progress?.Report("Cleaning up old files (this may take a while)...");
                            try
                            {
                                // Try to delete recursively
                                Directory.Delete(targetFolders, true);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error cleaning folders: {ex.Message}");
                                // If delete failed, maybe try to proceed or fail?
                                // Let's try to proceed, maybe it was just one file locked. 
                                // But if we can't clear, we might get mixed state.
                                
                                // Alternative: Rename it if delete fails?
                                try 
                                {
                                    if (Directory.Exists(targetFolders))
                                    {
                                        string trashName = $"{targetFolders}_Trash_{Guid.NewGuid()}";
                                        Directory.Move(targetFolders, trashName);
                                        // Try delete explicitly later or let OS cleanup temp eventually?
                                        // We can try to delete the moved folder in a fire-and-forget task
                                        Task.Run(() => { try { Directory.Delete(trashName, true); } catch { } });
                                    }
                                }
                                catch 
                                {
                                    // If move fails too, we just have to merge/overwrite
                                }
                            }
                        }
                        
                        progress?.Report("Restoring files...");
                        CopyDirectory(tempFolders, targetFolders);
                    }
                }
                finally
                {
                    // Cleanup
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            });
        }

        public void DeleteBackup(string filePath)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}
