using AutoMapper;
using ShadowrunAi.Core.Models;
using ShadowrunAi.Functions.DTOs;

namespace ShadowrunAi.Functions.Mapping;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<ChatSession, SessionResponseDto>()
            .ForMember(dest => dest.MessageCount, opt => opt.MapFrom(src => src.Turns.Count));
        CreateMap<ChatSessionSummary, SessionResponseDto>();
    }
}

