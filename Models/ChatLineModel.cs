using CatsAndMouseGame.Enums;

namespace CatsAndMouseGame.Models
{
    public class ChatLineModel
    {
        public string UserName { get; set; }
        public TeamEnum TeamId { get; set; }
        public string Message { get; set; }
    }
}
