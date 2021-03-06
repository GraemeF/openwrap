using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenWrap.Commands.Core;
using OpenWrap.Dependencies;
using OpenWrap.Repositories;
using OpenWrap.Services;

namespace OpenWrap.Commands.Wrap
{
    [Command(Verb = "update", Noun = "wrap")]
    public class UpdateWrapCommand : WrapCommand
    {
        bool? _system;

        [CommandInput(Position = 0)]
        public string Name { get; set; }

        [CommandInput]
        public bool System
        {
            get { return _system != null ? (bool)_system : false; }
            set { _system = value; }
        }

        bool? _project;

        [CommandInput]
        public bool Project
        {
            get { return _project == true || (_project == null && _system != true); }
            set { _project = value; }
        }

        public UpdateWrapCommand()
        {
        }
        public override IEnumerable<ICommandOutput> Execute()
        {
            var update = Enumerable.Empty<ICommandOutput>();
            if (Project)
                update = update.Concat(UpdateProjectPackages());
            if (System)
                update = update.Concat(UpdateSystemPackages());
            return Either(VerifyInputs)
                    .Or(update);
        }
        ICommandOutput VerifyInputs()
        {
            if (Project && Environment.ProjectRepository == null)
                return new Error("Project repository not found, cannot update. If you meant to update the system repository, use the -System input.");
            return null;
        }
        IEnumerable<ICommandOutput> UpdateSystemPackages()
        {
            if (!System) yield break;

            yield return new Result("Searching for updated packages...");
            foreach (var packageToSearch in CreateDescriptorForEachSystemPackage())
            {
                var sourceRepos = Environment.RemoteRepositories.Concat(Environment.CurrentDirectoryRepository).ToList();

                var resolveResult = PackageManager.TryResolveDependencies(packageToSearch, sourceRepos);
                var successful = resolveResult.Dependencies.Where(x => x.Package != null).ToList();
                resolveResult = new DependencyResolutionResult { IsSuccess = successful.Count > 0, Dependencies = successful };
                if (!resolveResult.IsSuccess)
                    continue;
                foreach (var m in PackageManager.CopyPackagesToRepositories(resolveResult, Environment.SystemRepository))
                    if (m is DependencyResolutionFailedResult)
                        yield return PackageNotFoundInRemote(m);

                    else
                        yield return m;
                foreach (var m in VerifyPackageCache(packageToSearch)) yield return m;
            }
        }

        IEnumerable<ICommandOutput> VerifyPackageCache(PackageDescriptor packageDescriptor)
        {
            return PackageManager.VerifyPackageCache(Environment, packageDescriptor);
        }

        IEnumerable<ICommandOutput> UpdateProjectPackages()
        {
            if (!Project)
                yield break;

            var sourceRepos = Environment.RemoteRepositories
                    .Concat(Environment.SystemRepository,
                            Environment.CurrentDirectoryRepository);

            var updateDescriptor = new PackageDescriptor(Environment.Descriptor);
            if (!string.IsNullOrEmpty(Name))
                updateDescriptor.Dependencies = updateDescriptor.Dependencies.Where(x => x.Name.Equals(Name, StringComparison.OrdinalIgnoreCase)).ToList();


            var resolvedPackages = PackageManager.TryResolveDependencies(
                updateDescriptor,
                sourceRepos);

            if (!resolvedPackages.IsSuccess)

            {
                foreach (var m in FailedUpdate(resolvedPackages, sourceRepos)) yield return m;
                yield break;
            }

            foreach (var m in resolvedPackages.GacConflicts(Environment.ExecutionEnvironment))
                yield return m;

            var copyResult = PackageManager.CopyPackagesToRepositories(
                resolvedPackages,
                Environment.ProjectRepository
                );
            foreach (var m in copyResult) yield return m;

            foreach (var m in PackageManager.VerifyPackageCache(Environment, updateDescriptor)) yield return m;
        }

        IEnumerable<ICommandOutput> FailedUpdate(DependencyResolutionResult resolvedPackages, IEnumerable<IPackageRepository> sourceRepos)
        {
            foreach(var notFoundPackage in resolvedPackages.Dependencies.Where(x=>x.Package == null))
                yield return new DependencyNotFoundInRepositories(notFoundPackage.Dependency, sourceRepos);

            var t = resolvedPackages.Dependencies
                    .Where(x => x.Package != null)
                    .GroupBy(x => x.Dependency.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(x => x.Count() > 1);
                    
            if (t.Count() > 0)
                yield return new DependenciesConflictMessage(t.ToList());
        }

        GenericMessage PackageNotFoundInRemote(ICommandOutput m)
        {
            return new GenericMessage("Package '{0}' doesn't exist in any remote repository.", ((DependencyResolutionFailedResult)m).Result.Dependencies.First().Dependency.Name)
            {
                Type = CommandResultType.Warning
            };
        }

        IEnumerable<PackageDescriptor> CreateDescriptorForEachSystemPackage()
        {


            return (
                           from systemPackage in Environment.SystemRepository.PackagesByName
                           let systemPackageName = systemPackage.Key
                           where ShouldIncludePackageInSystemUpdate(systemPackageName)
                           let maxPackageVersion = (
                                                           from versionedPackage in systemPackage
                                                           orderby versionedPackage.Version descending
                                                           select versionedPackage.Version
                                                   ).First()
                           select new PackageDescriptor
                           {
                               Dependencies =
                                           {
                                                   new PackageDependency
                                                   {
                                                           Name = systemPackageName,
                                                           VersionVertices = { new UpdatePackageVertex(maxPackageVersion) }
                                                   }
                                           }
                           }
                   ).ToList();
        }

        bool ShouldIncludePackageInSystemUpdate(string systemPackageName)
        {
            return string.IsNullOrEmpty(Name) ? true : Name.Equals(systemPackageName, StringComparison.OrdinalIgnoreCase);
        }
    }

    public class UpdatePackageVertex : VersionVertex
    {
        public UpdatePackageVertex(Version existingVersion) : base(existingVersion)
        {
        }
        public override bool IsCompatibleWith(Version version)
        {
            return (Version.Major == version.Major
                    && Version.Minor == version.Minor
                    && Version.Build == version.Build
                    && Version.Revision < version.Revision)
                   || 
                   (Version.Major == version.Major
                    && Version.Minor == version.Minor
                    && Version.Build < version.Build)
                   ||
                   (Version.Major == version.Major
                    && Version.Minor < version.Minor)
                   ||
                   (Version.Major < version.Major);
        }
    }

    public class DependencyNotFoundInRepositories : Warning
    {
        public PackageDependency Dependency { get; set; }
        public IEnumerable<IPackageRepository> Repositories { get; set; }

        public DependencyNotFoundInRepositories(PackageDependency dependency, IEnumerable<IPackageRepository> repositories)
        {
            Dependency = dependency;
            Repositories = repositories;
        }
        public override string ToString()
        {
            return string.Format("'{0}' not found in '{1}'.",Dependency, Repositories.Select(x => x.Name).Join(", "));
        }
    }
}
