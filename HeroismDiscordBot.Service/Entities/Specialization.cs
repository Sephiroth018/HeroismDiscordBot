﻿using System.ComponentModel.DataAnnotations;

namespace HeroismDiscordBot.Service.Entities
{
    public class Specialization
    {
        [Key]
        public int Id { get; set; }

        public string Name { get; set; }

        public int ItemLevel { get; set; }

        public string Role { get; set; }

        public virtual Character Character { get; set; }
    }
}