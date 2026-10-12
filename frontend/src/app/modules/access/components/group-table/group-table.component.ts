import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';

import { AccessGroup, apiErrorMessage, AuthService, GroupWrite } from '../../../../services/auth.service';
import { DeleteConfirmationDialogComponent } from '../../../../components/delete-confirmation-dialog/delete-confirmation-dialog.component';
import { matchesText } from '../../../../shared/search-filter';
import { isRowControlEvent } from '../../../../shared/row-click';
import { GroupEditDialogComponent } from '../group-edit-dialog/group-edit-dialog.component';

type GroupSourceFilter = 'all' | 'local' | 'directory';
type GroupRightsFilter =
  | 'all'
  | 'catalog-read'
  | 'catalog-write'
  | 'warehouse-read'
  | 'warehouse-write'
  | 'admin'
  | 'none';

interface GroupTableFilter {
  search: string;
  source: GroupSourceFilter;
  rights: GroupRightsFilter;
}

const EMPTY_GROUP_FILTER: GroupTableFilter = {
  search: '',
  source: 'all',
  rights: 'all'
};

@Component({
    selector: 'group-table',
    templateUrl: './group-table.component.html',
    changeDetection: ChangeDetectionStrategy.Eager,
    standalone: false
})
export class GroupTableComponent implements OnInit {
  groups: AccessGroup[] = [];
  filteredGroups: AccessGroup[] = [];
  isLoading = true;
  error = '';
  filter: GroupTableFilter = { ...EMPTY_GROUP_FILTER };

  readonly columns = ['name', 'source', 'description', 'rights', 'members', 'actions'];

  constructor(
    private auth: AuthService,
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
        this.applyView();
        this.isLoading = false;
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
      || this.filter.rights !== 'all';
  }

  onSearchChange(search: string): void {
    this.applyFilter({ ...this.filter, search });
  }

  onSourceChange(source: GroupSourceFilter): void {
    this.applyFilter({ ...this.filter, source });
  }

  onRightsChange(rights: GroupRightsFilter): void {
    this.applyFilter({ ...this.filter, rights });
  }

  clearFilters(): void {
    this.applyFilter({ ...EMPTY_GROUP_FILTER });
  }

  rights(group: AccessGroup): string[] {
    const parts: string[] = [];
    if (group.canWrite) {
      parts.push('Catalog read and write');
    } else if (group.canRead) {
      parts.push('Catalog read');
    }
    if (group.canWriteWarehouse) {
      parts.push('Warehouse read and write');
    } else if (group.canReadWarehouse) {
      parts.push('Warehouse read');
    }
    if (group.canAdmin) {
      parts.push('Administrators');
    }
    return parts.length ? parts : ['No access'];
  }

  add(): void {
    this.openEditor(null);
  }

  onRowClick(group: AccessGroup, event: Event): void {
    if (isRowControlEvent(event)) {
      return;
    }
    this.edit(group);
  }

  edit(group: AccessGroup): void {
    this.openEditor(group);
  }

  isAdmins(group: AccessGroup): boolean {
    return group.name.toLowerCase() === 'admins';
  }

  remove(group: AccessGroup): void {
    if (this.isAdmins(group)) {
      return;
    }

    this.dialog.open(DeleteConfirmationDialogComponent, { width: '440px' }).afterClosed().subscribe(confirmed => {
      if (!confirmed) {
        return;
      }

      this.auth.deleteGroup(group.id).subscribe({
        next: () => this.load(),
        error: error => this.error = apiErrorMessage(error, 'Could not delete the group')
      });
    });
  }

  private applyFilter(filter: GroupTableFilter): void {
    this.filter = filter;
    this.applyView();
  }

  private applyView(): void {
    this.filteredGroups = this.groups.filter(group => this.matches(group));
  }

  private matches(group: AccessGroup): boolean {
    const source = group.source === 'directory' ? 'directory' : 'local';
    if (this.filter.source !== 'all' && source !== this.filter.source) {
      return false;
    }
    if (!this.matchesRights(group)) {
      return false;
    }

    return matchesText(this.filter.search, [
      group.name,
      group.description,
      group.directoryDn,
      source === 'directory' ? 'Directory' : 'Local',
      group.memberCount,
      ...this.rights(group)
    ]);
  }

  private matchesRights(group: AccessGroup): boolean {
    switch (this.filter.rights) {
      case 'catalog-read':
        return group.canRead && !group.canWrite;
      case 'catalog-write':
        return group.canWrite;
      case 'warehouse-read':
        return !!group.canReadWarehouse && !group.canWriteWarehouse;
      case 'warehouse-write':
        return !!group.canWriteWarehouse;
      case 'admin':
        return !!group.canAdmin;
      case 'none':
        return !group.canRead
          && !group.canWrite
          && !group.canReadWarehouse
          && !group.canWriteWarehouse
          && !group.canAdmin;
      default:
        return true;
    }
  }

  private openEditor(group: AccessGroup | null): void {
    this.dialog.open(GroupEditDialogComponent, {
      width: '560px',
      data: { group }
    }).afterClosed().subscribe((result: GroupWrite | undefined) => {
      if (!result) {
        return;
      }

      const request = group
        ? this.auth.updateGroup(group.id, result)
        : this.auth.createGroup(result);
      request.subscribe({
        next: () => this.load(),
        error: error => this.error = apiErrorMessage(error, 'Could not save the group')
      });
    });
  }
}
