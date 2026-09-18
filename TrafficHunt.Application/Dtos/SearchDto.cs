using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Dtos
{
    public class SearchDto
    {
        public string Keyword { get; set; } = string.Empty;

        public string Prompt { get; set; } = string.Empty;

        public int MaxResult { get; set; } = 10;
    }
}
