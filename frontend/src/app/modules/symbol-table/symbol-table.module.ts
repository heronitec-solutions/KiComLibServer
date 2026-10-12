import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { SymbolTableComponent } from './components/symbol-table/symbol-table.component';
import { SymbolService } from './services/symbol.service';
import { SymbolDataSource } from './services/symbol.dataSource';
import { MatDialogModule } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { AppHeaderComponent } from '../../components/app-header/app-header.component';
import { HistoryModule } from '../history/history.module';

@NgModule({
  declarations: [
    SymbolTableComponent
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
    HistoryModule
  ],
  exports: [
    SymbolTableComponent
  ],
  providers: [
    SymbolService,
    SymbolDataSource
  ]
})
export class SymbolTableModule { }
