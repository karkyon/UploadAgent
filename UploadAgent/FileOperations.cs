using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UploadAgent.Models;

namespace UploadAgent
{
    public class FileOperations
    {
        private readonly AuditLogger  _logger;
        private readonly SecurityGuard _guard;
        private readonly StatsCounter  _stats;

        public FileOperations(AuditLogger logger, SecurityGuard guard, StatsCounter stats)
        {
            _logger = logger;
            _guard  = guard;
            _stats  = stats;
        }

        // ── ファイル移動 ──────────────────────────────────────────
        public MoveResponse MoveFiles(MoveRequest req)
        {
            var response = new MoveResponse();
            if (req.paths == null || req.paths.Count == 0) { response.success = false; return response; }

            foreach (var path in req.paths)
            {
                try
                {
                    if (!_guard.IsAllowedPath(path, out var reason))
                    {
                        response.failed.Add(new MoveFailure { path = path, error = $"Path not allowed: {reason}" });
                        _logger.Warn($"MOVE_REJECTED op={req.operator_id} path=\"{path}\" reason=\"{reason}\"");
                        _stats.IncrementError();
                        continue;
                    }
                    if (!File.Exists(path))
                    {
                        response.failed.Add(new MoveFailure { path = path, error = "File not found" });
                        _logger.Warn($"MOVE_NOTFOUND op={req.operator_id} path=\"{path}\"");
                        _stats.IncrementError();
                        continue;
                    }

                    var sourceDir = Path.GetDirectoryName(Path.GetFullPath(path));
                    var trashDir  = Path.Combine(sourceDir, ".machcore_trash");
                    EnsureTrashDir(trashDir);

                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var destName  = $"{timestamp}_{Path.GetFileName(path)}";
                    var destPath  = Path.Combine(trashDir, destName);
                    if (File.Exists(destPath))
                    {
                        timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssff");
                        destName  = $"{timestamp}_{Path.GetFileName(path)}";
                        destPath  = Path.Combine(trashDir, destName);
                    }

                    File.Move(path, destPath);
                    response.moved.Add(new MoveResult { original = path, destination = destPath });
                    _logger.Info($"MOVE op={req.operator_id} reason=\"{req.reason}\" src=\"{path}\" dst=\"{destPath}\"");
                    _stats.IncrementMoved();
                }
                catch (Exception ex)
                {
                    response.failed.Add(new MoveFailure { path = path, error = ex.Message });
                    _logger.Error($"MOVE_ERROR op={req.operator_id} path=\"{path}\" err=\"{ex.Message}\"");
                    _stats.IncrementError();
                }
            }

            response.success = response.failed.Count == 0 && response.moved.Count > 0;
            return response;
        }

        // ── リムーバブルドライブ一覧 ──────────────────────────────
        public DrivesResponse GetRemovableDrives()
        {
            var response = new DrivesResponse();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady))
                {
                    response.drives.Add(new DriveInfo2
                    {
                        letter   = d.Name.TrimEnd('\\', '/'),
                        label    = string.IsNullOrEmpty(d.VolumeLabel) ? "(No Label)" : d.VolumeLabel,
                        type     = "Removable",
                        free_gb  = Math.Round((double)d.AvailableFreeSpace / (1024 * 1024 * 1024), 1),
                        total_gb = Math.Round((double)d.TotalSize          / (1024 * 1024 * 1024), 1),
                    });
                }
            }
            catch (Exception ex) { _logger.Error($"DRIVES_ERROR err=\"{ex.Message}\""); }
            return response;
        }

        // ── Trash サイズ計算（全ドライブの .machcore_trash 合計）────
        public long GetTrashSizeBytes()
        {
            long total = 0;
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var trashDir = Path.Combine(d.RootDirectory.FullName, ".machcore_trash");
                    if (!Directory.Exists(trashDir)) continue;
                    foreach (var f in Directory.GetFiles(trashDir, "*", SearchOption.AllDirectories))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                }
            }
            catch { }
            return total;
        }

        // ── Trash 一括クリア ──────────────────────────────────────
        public (int deleted, long freedBytes) ClearTrash()
        {
            int  deleted    = 0;
            long freedBytes = 0;
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                {
                    var trashDir = Path.Combine(d.RootDirectory.FullName, ".machcore_trash");
                    if (!Directory.Exists(trashDir)) continue;
                    foreach (var f in Directory.GetFiles(trashDir))
                    {
                        try
                        {
                            var size = new FileInfo(f).Length;
                            File.Delete(f);
                            deleted++;
                            freedBytes += size;
                        }
                        catch { }
                    }
                }
                _logger.Info($"TRASH_CLEAR deleted={deleted} freed={freedBytes}bytes");
            }
            catch (Exception ex) { _logger.Error($"TRASH_CLEAR_ERROR err=\"{ex.Message}\""); }
            return (deleted, freedBytes);
        }

        private void EnsureTrashDir(string trashDir)
        {
            if (Directory.Exists(trashDir)) return;
            var di = Directory.CreateDirectory(trashDir);
            try { di.Attributes |= FileAttributes.Hidden; } catch { }
        }
    }
}
