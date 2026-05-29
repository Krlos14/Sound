using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Sounds.Data;
using Sounds.Model;
using Sounds.Utilidades;

public class IndexModel : PageModel
{
    // DbContext: puerta de entrada a la BD (consultar/insertar/actualizar)
    private readonly ApplicationDbContext _context;

    // Inyección de dependencias: .NET te entrega el context ya configurado
    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
    }

    [BindProperty(SupportsGet = true)]
    public string? q { get; set; }
   
    [BindProperty(SupportsGet = true)]
    public string? ActiveGroupCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? group { get; set; }

    public string Q => q ?? "";
    public string SelectedGroupCode => group ?? "";
    public string? SelectedGroupName { get; set; }
    public string? SelectedNowPlaying { get; set; }

    public List<string> Canciones { get; set; } = new();

    public List<GroupVm> Grupos { get; set; } = new();
    public List<grupo> grupo { get; set; } = new();

    public string? id_usuario { get; set; }
    private Utiles utiles = new Utiles();

    public void OnGet()
    {
        utiles.CargarUsuario(User);
        LoadMyGroups();
       

    }

    //POST
    public void OnPost()
    {

    }

    // Carga todos los grupos de la BD en la lista "grupo" y filtra segun el usuario
    public void LoadMyGroups()
    {

        var ids = _context.usergrupos.Where(us => us.id_user == utiles.id_usuario).Select(us => us.id_grupo).ToList();
        Grupos = (from g in _context.grupo
                  join ug in _context.usergrupos on g.id equals ug.id_grupo
                  where ug.id_user == utiles.id_usuario
                  select new GroupVm(
                      Code: g.code,
                      Name: g.name
                  )).ToList();
    }

    public record GroupVm(string Code, string Name);
}