using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Interfaces
{
    public interface ILLMService
    {
        Task<string> GenerateTextAsync(string prompt);
    }
}
