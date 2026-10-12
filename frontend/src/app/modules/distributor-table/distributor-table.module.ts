import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { FormsModule } from '@angular/forms';
import { MatTableModule } from '@angular/material/table';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { DistributorTableComponent } from './components/distributor-table/distributor-table.component';
import { DistributorService } from './services/distributor.service';
import { DistributorDataSource } from './services/distributor.dataSource';
import { DistributorEditDialogComponent } from './components/distributor-edit-dialog/distributor-edit-dialog.component';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { HistoryModule } from '../history/history.module';
import { TrimInputDirective } from '../../directives/trim-input.directive';




@NgModule({
  declarations: [
    DistributorTableComponent,
    DistributorEditDialogComponent
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
    MatCheckboxModule,
    MatDialogModule,
    MatIconModule,
    AppHeaderComponent,
    HistoryModule,
    TrimInputDirective
  ],
  exports: [
    DistributorTableComponent
  ],
  providers: [
    DistributorService,
    DistributorDataSource
  ]
})
export class DistributorTableModule { }
