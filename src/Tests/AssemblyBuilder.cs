using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Mono.Cecil;

namespace Tests
{
    public static class AssemblyBuilder
    {
        
        public static void CreateAssembly(Stream stream, string assemblyName, params Expression<Action<FluentTypeBuilder>>[] types)
        {
            AssemblyDefinition assembly = CreateAssembly(assemblyName, types);
            assembly.Write(stream);
        }

        public static Stream CreateAssemblyStream(string assemblyName, params Expression<Action<FluentTypeBuilder>>[] types)
        {
            var ms = new MemoryStream();
            CreateAssembly(ms, assemblyName, types);
            ms.Position = 0;
            return ms;
        }

        public static CustomAttribute CreateCustomAttribute<T>(ModuleDefinition module, Expression<Func<T>> attribute)
        {
            var init = attribute.Body as MemberInitExpression;
            if (init == null) throw new InvalidOperationException("Only constructing lambdas are supported");

            var ctor = init.NewExpression.Constructor;
            var ctorArgs = init.NewExpression.Arguments.Cast<ConstantExpression>().Select(x => new CustomAttributeArgument(module.Import(x.Type), x.Value)).ToList();
            var ctorParams = init.Bindings.Cast<MemberAssignment>().Select(x =>
                                                                           new CustomAttributeNamedArgument(
                                                                                   x.Member.Name,
                                                                                   new CustomAttributeArgument(module.Import(x.Expression.Type), ((ConstantExpression)x.Expression).Value))).ToList();
            var attrib = new CustomAttribute(module.Import(ctor));
            ctorArgs.ForEach(attrib.ConstructorArguments.Add);
            ctorParams.ForEach(attrib.Properties.Add);
            return attrib;
        }

        static AssemblyDefinition CreateAssembly(string assemblyName, Expression<Action<FluentTypeBuilder>>[] types)
        {
            var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(assemblyName, new Version("0.0.0.0")), assemblyName + ".dll", ModuleKind.Dll);
            foreach (var type in types)
            {
                var builder = new FluentTypeBuilder(assembly.MainModule, type.Parameters[0].Name);
                var config = type.Compile();
                config(builder);
                var typeDef = (TypeDefinition)builder;
                assembly.MainModule.Types.Add(typeDef);
            }
            return assembly;
        }
    }

    public delegate void FluentAssemblyBuilder(params Expression<Action<FluentTypeBuilder>>[] types);

    public class FluentTypeBuilder
    {
        readonly List<CustomAttribute> _attributes = new List<CustomAttribute>();
        readonly List<TypeReference> _interfaces = new List<TypeReference>();
        readonly List<FluentMethodBuilder> _methods = new List<FluentMethodBuilder>();
        readonly ModuleDefinition _module;
        readonly string _name;
        readonly List<FluentPropertyBuilder> _properties = new List<FluentPropertyBuilder>();
        TypeReference _baseClass;
        string _namespace;
        TypeAttributes _visibility;


        public FluentTypeBuilder(ModuleDefinition module, string name)
        {
            _module = module;
            _name = name;
            _visibility = TypeAttributes.Public;
        }

        public FluentTypeBuilder Internal
        {
            get
            {
                _visibility = TypeAttributes.NotPublic;
                return this;
            }
        }

        public static explicit operator TypeDefinition(FluentTypeBuilder builder)
        {
            var td = builder._baseClass != null
                             ? new TypeDefinition(builder._namespace, builder._name, GetTypeAttributes(builder), builder._baseClass)
                             : new TypeDefinition(builder._namespace, builder._name, GetTypeAttributes(builder));
            builder._methods.Select(_ => _.Build()).ToList().ForEach(td.Methods.Add);
            builder._attributes.ForEach(td.CustomAttributes.Add);
            builder._interfaces.ForEach(td.Interfaces.Add);
            builder._properties.Select(_ => (PropertyDefinition)_).ToList().ForEach(td.Properties.Add);
            return td;
        }

        public FluentTypeBuilder Attribute<T>(Expression<Func<T>> attribute) where T : Attribute
        {
            CustomAttribute attrib = AssemblyBuilder.CreateCustomAttribute(_module, attribute);
            _attributes.Add(attrib);
            return this;
        }

        public FluentTypeBuilder Implements<T>()
        {
            var interfaceType = _module.Import(typeof(T));
            _interfaces.Add(interfaceType);
            return this;
        }

        public FluentTypeBuilder Inherits<T>()
        {
            var targetType = typeof(T);

            _baseClass = _module.Import(typeof(T));
            return this;
        }

        public FluentTypeBuilder Method(params Expression<Action<FluentMethodBuilder>>[] methods)
        {
            foreach (var method in methods)
            {
                var methodBuilder = new FluentMethodBuilder(_module, method.Parameters[0].Name);
                method.Compile()(methodBuilder);
                _methods.Add(methodBuilder);
            }
            return this;
        }

        public FluentTypeBuilder Namespace(string @namespace)
        {
            _namespace = @namespace;
            return this;
        }

        public FluentTypeBuilder Property(params Expression<Action<FluentPropertyBuilder>>[] properties)
        {
            foreach (var property in properties)
            {
                var propBuilder = new FluentPropertyBuilder(_module, property.Parameters[0].Name);
                property.Compile()(propBuilder);
                _properties.Add(propBuilder);
            }
            return this;
        }

        static TypeAttributes GetTypeAttributes(FluentTypeBuilder builder)
        {
            return builder._visibility | TypeAttributes.Class;
        }
    }

    public class FluentPropertyBuilder
    {
        readonly List<CustomAttribute> _attributes = new List<CustomAttribute>();
        readonly ModuleDefinition _module;
        readonly string _name;
        bool _hasGetter;
        bool _hasSetter;
        TypeReference _returnType;

        public FluentPropertyBuilder(ModuleDefinition module, string name)
        {
            _module = module;
            _name = name;
        }

        public static explicit operator PropertyDefinition(FluentPropertyBuilder builder)
        {
            var pd = new PropertyDefinition(builder._name, PropertyAttributes.None, builder._returnType);
            if (builder._hasGetter)
                pd.GetMethod = new MethodDefinition("get_" + builder._name, MethodAttributes.Public, builder._returnType);
            if (builder._hasSetter)
                pd.SetMethod = new MethodDefinition("set_" + builder._name, MethodAttributes.Public, builder._returnType);
            builder._attributes.ForEach(pd.CustomAttributes.Add);
            return pd;
        }

        public FluentPropertyBuilder Attribute<T>(Expression<Func<T>> attribute) where T : Attribute
        {
            CustomAttribute attrib = AssemblyBuilder.CreateCustomAttribute(_module, attribute);
            _attributes.Add(attrib);
            return this;
        }

        public FluentPropertyBuilder Get()
        {
            _hasGetter = true;
            return this;
        }

        public FluentPropertyBuilder OfType<T>()
        {
            _returnType = _module.Import(typeof(T));
            return this;
        }

        public FluentPropertyBuilder Set()
        {
            _hasSetter = true;
            return this;
        }
    }

    public class FluentMethodBuilder
    {
        readonly ModuleDefinition _module;
        readonly string _name;
        MethodAttributes _instance;
        TypeReference _returnType;
        MethodAttributes _visibility;

        public FluentMethodBuilder(ModuleDefinition module, string name)
        {
            _module = module;
            _name = name;
            _visibility = MethodAttributes.Public;
            _returnType = _module.Import(typeof(void));
        }

        public FluentMethodBuilder Private
        {
            get
            {
                _visibility = MethodAttributes.Private;
                return this;
            }
        }

        public FluentMethodBuilder Static
        {
            get
            {
                _instance = MethodAttributes.Static;
                return this;
            }
        }

        public MethodDefinition Build()
        {
            return new MethodDefinition(_name, _visibility | _instance, _returnType ?? _module.Import(typeof(void)));
        }

        public FluentMethodBuilder Return<T>()
        {
            _returnType = _module.Import(typeof(T));

            return this;
        }
    }
}