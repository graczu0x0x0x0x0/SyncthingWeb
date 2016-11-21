﻿using System.Collections.Generic;
using System.IO;

namespace SyncthingWeb.Areas.Folders.Models
{
    public class StorageContentViewModel
    {
        public StorageContentViewModel(IEnumerable<DirectoryEntryContext> dirs, IEnumerable<FileEntryContext> files)
        {
            this.Directories = dirs;
            this.Files = files;
        }

        public IEnumerable<DirectoryEntryContext> Directories { get; }
        public IEnumerable<FileEntryContext> Files { get;}

        public string FolderId { get; set; }
        public string CurrPath { get; set; }

        public string PathFor(string folder)
        {
            return Path.Combine(this.CurrPath, folder);
        }
    }
}