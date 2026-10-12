import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatTableModule } from '@angular/material/table';
import { MatTabsModule } from '@angular/material/tabs';

import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { PartTableModule } from '../part-table/part-table.module';
import { BomDiffComponent } from './components/bom-diff/bom-diff.component';
import { BomTableComponent } from './components/bom-table/bom-table.component';
import { ProjectAccessDialogComponent } from './components/project-access-dialog/project-access-dialog.component';
import { ProjectDetailComponent } from './components/project-detail/project-detail.component';
import { ProjectEditDialogComponent } from './components/project-edit-dialog/project-edit-dialog.component';
import { ProjectTableComponent } from './components/project-table/project-table.component';
import { ReleaseDialogComponent } from './components/release-dialog/release-dialog.component';
import { RevisionTableComponent } from './components/revision-table/revision-table.component';
import { SchematicViewComponent } from './components/schematic-view/schematic-view.component';
import { VisualDiffViewerComponent } from './components/visual-diff-viewer/visual-diff-viewer.component';

@NgModule({
  declarations: [
    ProjectTableComponent,
    ProjectDetailComponent,
    RevisionTableComponent,
    BomTableComponent,
    BomDiffComponent,
    SchematicViewComponent,
    VisualDiffViewerComponent,
    ProjectEditDialogComponent,
    ProjectAccessDialogComponent,
    ReleaseDialogComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatTableModule,
    MatTabsModule,
    AppHeaderComponent,
    PartTableModule
  ],
  exports: [
    ProjectTableComponent,
    ProjectDetailComponent,
    VisualDiffViewerComponent
  ]
})
export class ProjectsModule { }
