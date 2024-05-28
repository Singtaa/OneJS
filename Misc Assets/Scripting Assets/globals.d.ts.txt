declare global {
    interface Console {
        assert(condition?: boolean, ...data: any[]): void;
        error(...data: any[]): void;
        info(...data: any[]): void;
        log(...data: any[]): void;
        time(label?: string): void;
        timeEnd(label?: string): void;
        trace(...data: any[]): void;
        warn(...data: any[]): void;
    }

    var console: Console;

    type TimerHandler = string | Function;

    function getType(obj: any): any

    function setInterval(handler: TimerHandler, timeout?: number, ...arguments: any[]): number;
    function clearInterval(id: number | undefined): void
    function setTimeout(handler: TimerHandler, timeout?: number, ...arguments: any[]): number;
    function clearTimeout(id: number | undefined): void
    function requestAnimationFrame(callback: Function): number
    function cancelAnimationFrame(handle: number): void;

    var document: CS.OneJS.Dom.Document;
    var resource: CS.OneJS.Resource;

    function atob(encodedData: string): string;
    function btoa(rawData: string): string;

    function require(name: string): any;

    type HTMLElement = CS.OneJS.Dom.Dom;

    interface CharacterData {
        data: string
        readonly length: number
        readonly ownerDocument: CS.OneJS.Dom.Document
        appendData(data: string): void
        deleteData(offset: number, count: number): void
        insertData(offset: number, data: string): void
        replaceData(offset: number, count: number, data: string): void
        substringData(offset: number, count: number): string
    }

    interface Text extends CharacterData {
        /** Returns the combined data of all direct Text node siblings. */
        readonly wholeText: string
        /** Splits data at the given offset and returns the remainder as Text node. */
        splitText(offset: number): Text
    }

}

export { }