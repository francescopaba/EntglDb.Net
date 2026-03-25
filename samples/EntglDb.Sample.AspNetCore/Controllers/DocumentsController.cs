using EntglDb.Sample.Shared;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EntglDb.Sample.AspNetCore.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly SampleDbContext _db;

    public DocumentsController(SampleDbContext db)
    {
        _db = db;
    }

    [HttpGet("{collection}/{id}")]
    public async Task<IActionResult> GetDocument(string collection, string id)
    {
        switch (collection.ToLower())
        {
            case "users":
                var user = await _db.Users.FindByIdAsync(id);
                return user is not null ? Ok(user) : NotFound();
            case "todolists":
                var todo = await _db.TodoLists.FindByIdAsync(id);
                return todo is not null ? Ok(todo) : NotFound();
            default:
                return NotFound();
        }
    }

    [HttpPost("{collection}")]
    public async Task<IActionResult> SaveDocument(string collection, [FromBody] JsonElement content)
    {
        string id = content.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();

        switch (collection.ToLower())
        {
            case "users":
                var user = JsonSerializer.Deserialize<User>(content.GetRawText());
                if (user == null) return BadRequest();
                user.Id = id;
                await _db.Users.InsertAsync(user);
                break;
            case "todolists":
                var list = JsonSerializer.Deserialize<TodoList>(content.GetRawText());
                if (list == null) return BadRequest();
                list.Id = id;
                await _db.TodoLists.InsertAsync(list);
                break;
            default:
                return NotFound();
        }
        await _db.SaveChangesAsync();
        return Ok(new { Message = "Saved", Id = id });
    }

    [HttpGet("{collection}")]
    public async Task<IActionResult> ListDocuments(string collection)
    {
        switch (collection.ToLower())
        {
            case "users":
                var users = new List<User>();
                await foreach (var u in _db.Users.FindAllAsync())
                    users.Add(u);
                return Ok(users);
            case "todolists":
                var lists = new List<TodoList>();
                await foreach (var t in _db.TodoLists.FindAllAsync())
                    lists.Add(t);
                return Ok(lists);
            default:
                return NotFound();
        }
    }
}
