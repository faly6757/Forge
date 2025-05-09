using System;

namespace WebApplication1.Models
{
    public class Document
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string FileType { get; set; }
        public string ForgeUrn { get; set; }
        public DateTime UploadDate { get; set; }
    }
} 