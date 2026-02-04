using System.Diagnostics;

namespace DeskFolder.Helpers;

/// <summary>
/// Removes the Zone.Identifier alternate data stream from files to prevent
/// Windows "trusted source" security warnings when opening files.
/// </summary>
public static class FileUnblocker
{
    /// <summary>
    /// Unblocks a single file by removing the Zone.Identifier alternate data stream.
    /// </summary>
    /// <param name="filePath">Full path to the file to unblock</param>
    /// <returns>True if successful or if file was already unblocked, false if an error occurred</returns>
    public static bool UnblockFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        try
        {
            // The Zone.Identifier is stored in an alternate data stream
            var zoneIdentifierPath = filePath + ":Zone.Identifier";
            
            // Check if the alternate data stream exists
            if (File.Exists(zoneIdentifierPath))
            {
                File.Delete(zoneIdentifierPath);
            }
            
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Unblocks all files in a directory recursively.
    /// </summary>
    /// <param name="directoryPath">Full path to the directory</param>
    /// <param name="recursive">Whether to process subdirectories</param>
    /// <returns>Number of files successfully unblocked</returns>
    public static int UnblockDirectory(string directoryPath, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return 0;

        int unblockedCount = 0;

        try
        {
            // Unblock all files in the directory
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", searchOption))
            {
                if (UnblockFile(file))
                {
                    unblockedCount++;
                }
            }
        }
        catch
        {
        }

        return unblockedCount;
    }

    /// <summary>
    /// Unblocks multiple files.
    /// </summary>
    /// <param name="filePaths">Collection of file paths to unblock</param>
    /// <returns>Number of files successfully unblocked</returns>
    public static int UnblockFiles(IEnumerable<string> filePaths)
    {
        if (filePaths == null)
            return 0;

        int unblockedCount = 0;

        foreach (var filePath in filePaths)
        {
            if (UnblockFile(filePath))
            {
                unblockedCount++;
            }
        }

        return unblockedCount;
    }
}
