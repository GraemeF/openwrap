using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OpenWrap.Commands
{
    public class AttributeBasedCommandDescriptor : ICommandDescriptor
    {
        readonly Type _commandType;

        public AttributeBasedCommandDescriptor(Type commandType)
        {
            _commandType = commandType;
            var attribute = commandType.GetAttribute<CommandAttribute>() ?? new CommandAttribute();
            Noun = attribute.Noun ?? DeductNounFromNamespace(commandType);
            Verb = attribute.Verb ?? commandType.Name;

            var commandResourceKey = attribute.Verb + "-" + attribute.Noun;
            Description = attribute.Description ?? CommandDocumentation.GetCommandDescription(commandType, commandResourceKey);

            Inputs = (from pi in commandType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                      let inputAttrib = pi.GetAttribute<CommandInputAttribute>()
                      where inputAttrib != null
                      let values = inputAttrib ?? new CommandInputAttribute()
                      let inputName = values.Name ?? pi.Name
                      select (ICommandInputDescriptor)new CommandInputDescriptor
                      {
                          Name = inputName,
                          IsRequired = values.IsRequired,
                          Description = values.DisplayName ?? CommandDocumentation.GetCommandDescription(commandType, commandResourceKey+"-"+inputName),
                          Position = values.Position,
                          Property = pi
                      }).ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        }

        string DeductNounFromNamespace(Type commandType)
        {
            int dotIndex = commandType.Namespace.LastIndexOf('.');
            return dotIndex != -1 ? commandType.Namespace.Substring(dotIndex=1) : commandType.Namespace;
        }

        public string Noun { get; set; }
        public string Verb { get; set; }
        public string Description { get; set; }
        public IDictionary<string, ICommandInputDescriptor> Inputs { get; set; }
        public ICommand Create()
        {
            return (ICommand)Activator.CreateInstance(_commandType);
            
        }
    }

    public class AttributeBasedCommandDescriptor<T> : AttributeBasedCommandDescriptor
        where T:ICommand
    {
        public AttributeBasedCommandDescriptor() : base(typeof(T))
        {
        }
    }
}