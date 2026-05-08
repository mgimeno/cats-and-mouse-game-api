using CatsAndMouseApi.Enums;

namespace CatsAndMouseApi.Models
{
    public class ChatLineModel
    {
        public string UserName { get; set; } = string.Empty;
        public TeamEnum TeamId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
