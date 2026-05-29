

namespace Sounds.Model
{
    public class grupo
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string code { get; set; }
        
       
    }
    public class usergrupos
    {
        public int id { get; set; }
        public int id_grupo { get; set; } 
        public string id_user { get; set; }
    }

}
