import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { ManufacturerTableComponent } from './components/manufacturer-table/manufacturer-table.component';
import { ManufacturerService } from './services/manufacturer.service';
import { ManufacturerDataSource } from './services/manufacturer.dataSource';
import { ManufacturerEditDialogComponent } from './components/manufacturer-edit-dialog/manufacturer-edit-dialog.component';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { HistoryModule } from '../history/history.module';
import { TrimInputDirective } from '../../directives/trim-input.directive';




@NgModule({
  declarations: [
    ManufacturerTableComponent,
    ManufacturerEditDialogComponent
  ],
  imports: [
    CommonModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatTableModule,
    MatSortModule,
    MatPaginatorModule,
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    AppHeaderComponent,
    HistoryModule,
    TrimInputDirective
  ],
  exports: [
    ManufacturerTableComponent
  ],
  providers: [
    ManufacturerService,
    ManufacturerDataSource
  ]
})
export class ManufacturerTableModule { }
