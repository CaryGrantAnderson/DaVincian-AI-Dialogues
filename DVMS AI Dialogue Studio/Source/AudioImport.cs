using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Dapper;

namespace DVMS.AIDialogueStudio
{
    public sealed partial class RecordingRepository
    {
        public List<string> ImportedAudioPaths() { using (var db = Open()) return db.Query<string>("SELECT RelativePath FROM MediaAssets WHERE Kind='ImportedAudio'").Select(p => Path.Combine(Folder, p.Replace('/', Path.DirectorySeparatorChar))).ToList(); }
        public void ImportAudio(string sourceFile)
        {
            string id = Guid.NewGuid().ToString("N"), extension = Path.GetExtension(sourceFile).ToLowerInvariant();
            string relative = "Media/Imported/" + id + extension;
            string destination = Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar));
            string hash;
            using (var input = File.OpenRead(sourceFile))
            using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
            using (var input = File.OpenRead(destination)) using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            using (var db = Open()) using (var tx = db.BeginTransaction())
            {
                db.Execute("INSERT INTO Contributions VALUES (@id,'ImportedAudio',@extension,@relative,@hash,'',@now)", new { id, extension, relative, hash, now = DateTime.UtcNow.ToString("O") }, tx);
                db.Execute("INSERT INTO MediaAssets VALUES (@id,'ImportedAudio',@relative,@hash,NULL,NULL,NULL)", new { id, relative, hash }, tx);
                tx.Commit();
            }
            Save(Load(), "Imported audio source (no transcript supplied)");
        }
    }
}
