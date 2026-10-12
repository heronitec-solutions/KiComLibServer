import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatSortModule } from '@angular/material/sort';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { ProductImageComponent } from '../../components/product-image/product-image.component';
import { TrimInputDirective } from '../../directives/trim-input.directive';
import { WarehouseTableComponent } from './components/warehouse-table/warehouse-table.component';
import { WarehouseEditDialogComponent } from './components/warehouse-edit-dialog/warehouse-edit-dialog.component';
import { WarehouseViewDialogComponent } from './components/warehouse-view-dialog/warehouse-view-dialog.component';
import { WarehouseService } from './services/warehouse.service';
import { WarehouseDataSource } from './services/warehouse.dataSource';

@NgModule({
  declarations: [
    WarehouseTableComponent,
    WarehouseEditDialogComponent,
    WarehouseViewDialogComponent
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
    ProductImageComponent,
    TrimInputDirective
  ],
  exports: [
    WarehouseTableComponent
  ],
  providers: [
    WarehouseService,
    WarehouseDataSource
  ]
})
export class WarehouseModule { }
