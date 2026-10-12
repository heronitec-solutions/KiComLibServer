import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';

import { AccessGroup, apiErrorMessage, AppUser, AuthService, UserWrite } from '../../../../services/auth.service';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { matchesText } from '../../../../shared/search-filter';
import { isRowControlEvent } from '../../../../shared/row-click';
import { UserEditDialogComponent } from '../user-edit-dialog/user-edit-dialog.component';

type UserSourceFilter = 'all' | 'local' | 'directory';
type UserStatusFilter = 'all' | 'active' | 'inactive';
type UserAccessFilter =
  | 'all'
  | 'catalog-read'
  | 'catalog-write'
  | 'warehouse-read'
  | 'warehouse-write'
  | 'admin'
  | 'none';

interface UserTableFilter {
  search: string;
  source: UserSourceFilter;
  status: UserStatusFilter;
  groupId: number | 'all';
  access: UserAccessFilter;
}

const EMPTY_USER_FILTER: UserTableFilter = {
  search: '',
  source: 'all',
  status: 'all',
  groupId: 'all',
  access: 'all'
};

@Component({
    selector: 'user-table',
    templateUrl: './user-table.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class UserTableComponent implements OnInit {
  users: AppUser[] = [];
  filteredUsers: AppUser[] = [];
  groups: AccessGroup[] = [];
  isLoading = true;
  error = '';
  filter: UserTableFilter = { ...EMPTY_USER_FILTER };

  readonly columns = ['username', 'source', 'groups', 'access', 'status', 'actions'];

  constructor(
    public auth: AuthService,
    private dialog: MatDialog
  ) { }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.isLoading = true;
    this.error = '';
    this.auth.getGroups().subscribe({
      next: groups => {
        this.groups = groups;
        this.auth.getUsers().subscribe({
          next: users => {
            this.users = users;
            this.applyView();
            this.isLoading = false;
          },
          error: error => {
            this.error = apiErrorMessage(error, 'Could not load users');
            this.isLoading = false;
          }
        });
      },
      error: error => {
        this.error = apiErrorMessage(error, 'Could not load groups');
        this.isLoading = false;
      }
    });
  }

  get hasActiveFilters(): boolean {
    return !!this.filter.search.trim()
      || this.filter.source !== 'all'
      || this.filter.status !== 'all'
      || this.filter.groupId !== 'all'
      || this.filter.access !== 'all';
  }

  onSearchChange(search: string): void {
    this.applyFilter({ ...this.filter, search });
  }

  onSourceChange(source: UserSourceFilter): void {
    this.applyFilter({ ...this.filter, source });
  }

  onStatusChange(status: UserStatusFilter): void {
    this.applyFilter({ ...this.filter, status });
  }

  onAccessChange(access: UserAccessFilter): void {
    this.applyFilter({ ...this.filter, access });
  }

  onGroupChange(groupId: number | string): void {
    this.applyFilter({
      ...this.filter,
      groupId: groupId === 'all' ? 'all' : Number(groupId)
    });
  }

  clearFilters(): void {
    this.applyFilter({ ...EMPTY_USER_FILTER });
  }

  groupNames(user: AppUser): string {
    return user.groups.length ? user.groups.map(group => group.name).join(', ') : '—';
  }

  accessRights(user: AppUser): string[] {
    const parts: string[] = [];
    if (user.canWrite) {
      parts.push('Catalog read and write');
    } else if (user.canRead) {
      parts.push('Catalog read');
    }
    if (user.canWriteWarehouse) {
      parts.push('Warehouse read and write');
    } else if (user.canReadWarehouse) {
      parts.push('Warehouse read');
    }
    if (user.isAdmin) {
      parts.push('Administrators');
    }
    return parts.length ? parts : ['No access'];
  }

  add(): void {
    this.openEditor(null);
  }

  onRowClick(user: AppUser, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.edit(user);
  }

  edit(user: AppUser): void {
    this.openEditor(user);
  }

  remove(user: AppUser): void {
    this.dialog.open(DeleteConfirmationDialogComponent, { width: '440px' }).afterClosed().subscribe(confirmed => {
      if (!confirmed) {
        return;
      }

      this.auth.deleteUser(user.id).subscribe({
        next: () => this.load(),
        error: error => this.error = apiErrorMessage(error, 'Could not delete the user')
      });
    });
  }

  private applyFilter(filter: UserTableFilter): void {
    this.filter = filter;
    this.applyView();
  }

  private applyView(): void {
    this.filteredUsers = this.users.filter(user => this.matches(user));
  }

  private matches(user: AppUser): boolean {
    const source = user.source === 'directory' ? 'directory' : 'local';
    if (this.filter.source !== 'all' && source !== this.filter.source) {
      return false;
    }
    if (this.filter.status === 'active' && !user.isActive) {
      return false;
    }
    if (this.filter.status === 'inactive' && user.isActive) {
      return false;
    }
    if (this.filter.groupId !== 'all' && !user.groups.some(group => group.id === this.filter.groupId)) {
      return false;
    }
    if (!this.matchesAccess(user)) {
      return false;
    }

    return matchesText(this.filter.search, [
      user.username,
      user.displayName,
      source === 'directory' ? 'Directory' : 'Local',
      user.isActive ? 'Active' : 'Inactive',
      ...user.groups.map(group => group.name),
      ...this.accessRights(user)
    ]);
  }

  private matchesAccess(user: AppUser): boolean {
    switch (this.filter.access) {
      case 'catalog-read':
        return user.canRead && !user.canWrite;
      case 'catalog-write':
        return user.canWrite;
      case 'warehouse-read':
        return user.canReadWarehouse && !user.canWriteWarehouse;
      case 'warehouse-write':
        return user.canWriteWarehouse;
      case 'admin':
        return user.isAdmin;
      case 'none':
        return !user.canRead
          && !user.canWrite
          && !user.canReadWarehouse
          && !user.canWriteWarehouse
          && !user.isAdmin;
      default:
        return true;
    }
  }

  private openEditor(user: AppUser | null): void {
    this.dialog.open(UserEditDialogComponent, {
      width: '640px',
      data: { user, groups: this.groups }
    }).afterClosed().subscribe((result: UserWrite | undefined) => {
      if (!result) {
        return;
      }

      const request = user
        ? this.auth.updateUser(user.id, result)
        : this.auth.createUser(result);
      request.subscribe({
        next: () => this.load(),
        error: error => this.error = apiErrorMessage(error, 'Could not save the user')
      });
    });
  }
}
