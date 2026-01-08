using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Persistence
{
    public class CurrentCampusService : ICurrentCampusService
    {
        public Guid CampusId { get; set; }
        public CurrentCampusService(IHttpContextAccessor accessor)
        {
            var campusClaim = accessor.HttpContext?
                .User.FindFirst("campus_id")?.Value;

            CampusId = Guid.TryParse(campusClaim, out var id)
                ? id
                : Guid.Empty;
        }
    }
}
