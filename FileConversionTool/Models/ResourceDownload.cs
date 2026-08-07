namespace FileConversionTool.Models;

public partial class ResourceDownload
{
    public int ID { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public string FilePath { get; set; } = null!;

    public int CategoryID { get; set; }

    public int? ThumbnailID { get; set; }

    public int? FolderId { get; set; }
}
