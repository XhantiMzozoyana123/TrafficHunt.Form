using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace TrafficHunt.Application.Dtos
{
    /// <summary>
    /// Maps the response returned by the Ollama text generation endpoint
    /// (POST http://46.202.170.203:11434/api/generate with
    /// { "model": "llama3:latest", "prompt": "...", "stream": false }), for example:
    /// {
    ///   "model": "llama3:latest",
    ///   "created_at": "2026-09-20T10:37:57.466532678Z",
    ///   "response": "Hi there! ...",
    ///   "done": true,
    ///   "done_reason": "stop",
    ///   "context": [ ... ],
    ///   "total_duration": 11124581920,
    ///   "load_duration": 327489531,
    ///   "prompt_eval_count": 14,
    ///   "prompt_eval_duration": 1299628000,
    ///   "eval_count": 55,
    ///   "eval_duration": 9493608000
    /// }
    /// </summary>
    public class OllamaResponseDto
    {
        /// <summary>The elapsed time spent generating the reply, in nanoseconds.</summary>
        [JsonPropertyName("total_duration")]
        public long TotalDuration { get; set; }

        /// <summary>The elapsed time spent loading the model, in nanoseconds.</summary>
        [JsonPropertyName("load_duration")]
        public long LoadDuration { get; set; }

        /// <summary>The number of tokens in the prompt.</summary>
        [JsonPropertyName("prompt_eval_count")]
        public int PromptEvalCount { get; set; }

        /// <summary>The elapsed time spent evaluating the prompt, in nanoseconds.</summary>
        [JsonPropertyName("prompt_eval_duration")]
        public long PromptEvalDuration { get; set; }

        /// <summary>The number of tokens generated in the reply.</summary>
        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; }

        /// <summary>The elapsed time spent generating the reply, in nanoseconds.</summary>
        [JsonPropertyName("eval_duration")]
        public long EvalDuration { get; set; }

        /// <summary>The model that produced the reply, for example "llama3:latest".</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>When the reply was generated.</summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>The generated text. This is the only field the app consumes.</summary>
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        /// <summary>True when the generation finished (streaming is disabled, so this is always true).</summary>
        [JsonPropertyName("done")]
        public bool Done { get; set; }

        /// <summary>The reason the generation stopped, for example "stop".</summary>
        [JsonPropertyName("done_reason")]
        public string DoneReason { get; set; } = string.Empty;
    }
}
