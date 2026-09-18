using System;
using System.Collections.Generic;
using System.Text;
using TrafficHunt.Application.Dtos;

namespace TrafficHunt.Application.Interfaces
{
    public interface IExtractService
    {
        Task ExtractVideoAsync(SearchDto searchDto);
    }
}
