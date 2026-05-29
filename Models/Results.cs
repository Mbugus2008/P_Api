namespace ParcelAPI.Models
{
    public class Results<T>
    {
        public int Code { get; set; }
        public string? Desc { get; set; }
        public T? Contents { get; set; }
    }
}