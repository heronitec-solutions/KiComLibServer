import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { CadModelTableComponent } from './components/cad-model-table/cad-model-table.component';
import { CadModelService } from './services/cad-model.service';
import { CadModelDataSource } from './services/cad-model.dataSource';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { HistoryModule } from '../history/history.module';

@NgModule({
  declarations: [
    CadModelTableComponent
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
    MatTooltipModule,
    AppHeaderComponent,
    HistoryModule
  ],
  exports: [
    CadModelTableComponent
  ],
  providers: [
    CadModelService,
    CadModelDataSource
  ]
})
export class CadModelTableModule { }
