using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Sounds.Model;

namespace Sounds.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
    {
     
        public DbSet<grupo> grupo { get; set; }
        public DbSet<usergrupos> usergrupos { get; set; }
      
    }
}
