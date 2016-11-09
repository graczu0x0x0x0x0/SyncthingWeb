﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using SyncthingWeb.Caching;
using SyncthingWeb.Commands;
using SyncthingWeb.Commands.Implementation.Events;
using SyncthingWeb.Commands.Implementation.Users;
using SyncthingWebUI.Commands.Implementations.Users;
using SyncthingWebUI.Permissions;

namespace SyncthingWeb.Permissions
{
    internal class DefaultPermissionResolver : IPermissionResolver, IUserRolesChanged
    {
        private const string AllPermissionsKey = "all-permissions";
        private const string AllPermissionsDictKey = "all-permissions-dictionary";
        private const string UserPermissionsTemplate = "user-permission-{0}";

        private readonly IPermissionProvider permissionProvider;
        private readonly ICache cache;
        private readonly ICommandFactory commandFactory;

        public DefaultPermissionResolver(IPermissionProvider provider, ICache cache, ICommandFactory commandFactory)
        {
            this.permissionProvider = provider;
            this.cache = cache;
            this.commandFactory = commandFactory;
        }


        public IEnumerable<Permission> All => this.AllPermissions;


        private HashSet<Permission> AllPermissions
        {
            get { return this.cache.Get(AllPermissionsKey, context => this.CollectAllPermission()); }
        }


        private async Task<HashSet<Permission>> CollectForUser(string userId)
        {
            var allPermDict = this.cache.Get(AllPermissionsDictKey,
                context => this.AllPermissions.ToDictionary(p => p.Name));

            var roleIds =
                (await this.commandFactory.Create<GetUserRolesCommand>().Setup(userId).GetAsync()).Select(
                    rl => rl.RoleId).ToArray();
            var permsName =
                await this.commandFactory.Create<QueryRolesPermissionsNamesCommand>().Setup(roleIds).ExecuteAsync();

            var userPermissions = new HashSet<Permission>();

            var travelStack = new Stack<Permission>(this.AllPermissions.Where(p => permsName.Contains(p.Name)));
            while (travelStack.Any())
            {
                var curr = travelStack.Pop();
                if (userPermissions.Contains(curr)) continue;

                userPermissions.Add(curr);

                foreach (var impliedName in curr.Implies)
                {
                    travelStack.Push(allPermDict[impliedName]);
                }
            }


            return userPermissions;
        }

        public Task<HashSet<Permission>> GetForUser(string userId)
        {
            return this.cache.Get(PermissionForUserKey(userId), ctx => this.CollectForUser(userId));
        }

        public Task SetForRole(string id, IEnumerable<string> perms)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (perms == null) throw new ArgumentNullException(nameof(perms));

            return
                this.commandFactory.Create<SetPermissionsForRoleCommand>()
                    .Setup(id, perms as string[] ?? perms.ToArray())
                    .ExecuteAsync()
                    .ContinueWith(
                        async t => await this.commandFactory.Create<QueryUsersIdInRolesCommand>().ExecuteAsync())
                    .ContinueWith(
                        t =>
                        {
                            foreach (var usr in t.Result.Result)
                            {
                                this.UserPermissionsChanged(usr);
                            }
                        });
        }

        public void UserPermissionsChanged(string userId)
        {
            this.cache.Signal(PermissionForUserKey(userId));
        }

        public async Task<bool> Authorize(Permission permission, string userId)
        {
            return (await this.GetForUser(userId)).Contains(permission);
        }

        public async Task<HashSet<Permission>> GetForRole(string id)
        {
            // TODO cache
            var permissionsNamesSet =
                await this.commandFactory.Create<QueryRolesPermissionsNamesCommand>().Setup(id).ExecuteAsync();

            return new HashSet<Permission>(this.AllPermissions.Where(p => permissionsNamesSet.Contains(p.Name)));
        }

        public Task Added(string userId)
        {
            this.cache.Signal(PermissionForUserKey(userId));
            return Task.FromResult(true);
        }

        public Task Remove(string userId)
        {
            this.cache.Signal(PermissionForUserKey(userId));
            return Task.FromResult(true);
        }

        private static string PermissionForUserKey(string userId)
        {
            return string.Format(UserPermissionsTemplate, userId);
        }

        private HashSet<Permission> CollectAllPermission()
        {
            var coll = new Collection<Permission>();

            this.permissionProvider.Register(coll);

            return new HashSet<Permission>(coll);
        }
    }
}