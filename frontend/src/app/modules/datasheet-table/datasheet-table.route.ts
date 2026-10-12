import { ChangeDetectionStrategy, Component } from '@angular/core';
import { DatasheetTableModule } from './datasheet-table.module';

@Component({
    selector: 'datasheet-table-route',
    imports: [DatasheetTableModule],
    template: '<datasheet-table></datasheet-table>',
    styles: [':host { display: contents; }'],
    changeDetection: ChangeDetectionStrategy.Eager
})
export class DatasheetTableRouteComponent {}
