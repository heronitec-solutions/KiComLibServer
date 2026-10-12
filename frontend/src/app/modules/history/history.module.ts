import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';

import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { CommitDataSource } from './services/commit.dataSource';
import { CommitTableComponent } from './components/commit-table/commit-table.component';
import { CommitDetailComponent } from './components/commit-detail/commit-detail.component';
import { DiffViewComponent } from './components/diff-view/diff-view.component';
import { RevisionHistoryDialogComponent } from './components/revision-history-dialog/revision-history-dialog.component';

@NgModule({
  declarations: [
    CommitTableComponent,
    CommitDetailComponent,
    DiffViewComponent,
    RevisionHistoryDialogComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    MatPaginatorModule,
    MatTableModule,
    MatDialogModule,
    MatIconModule,
    MatButtonModule,
    MatTooltipModule,
    AppHeaderComponent
  ],
  exports: [
    CommitTableComponent,
    CommitDetailComponent,
    DiffViewComponent,
    RevisionHistoryDialogComponent
  ],
  providers: [
    CommitDataSource
  ]
})
export class HistoryModule { }
