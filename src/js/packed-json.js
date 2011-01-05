PackedJson = {
	/**
	 * Convert a byte into an array of bits
	 */
	_byteToBits: function(b) {
		var arr = [];
		for (var i=0; i<8; i++) {
			arr.unshift(
				(b >> i) & 0x01
				? true
				: false
			);
		}
		return arr;
	},
	/**
	 * Convert an array of bits into a byte
	 */
	_bitsToByte: function(bits) {

	},
	/**
	 * Find the power of two at a given number
	 */
	_powerOf2: function(n) {
		var x = 1;
		for (var i=0; i<n; i++) {
			x *= 2;
		}
		return x;
	},
	/**
	 * Unpack a byte from a buffer
	 */
	unpackByte: function(buffer) {
		return buffer.read();
	},
	/**
	 * Unpack a char from a buffer. Chars are stored as variable ints
	 */
	unpackChar: function(buffer) {
		var b = this.unpackVariableInt(buffer, false);
		return String.fromCharCode(b);
	},
	/**
	 * Unpack a date time.
	 */
	unpackDateTime: function(buffer) {
		throw "Not Implemented";
	},
	/**
	 * Unpack a fixed point number
	 */
	unpackFixed: function(len, buffer) {
		var scale = len == 32 ? 100000000 : 10000000000000000;
		var n = this.unpackVariableInt(buffer, true);
		return n / scale;
	},
	/**
	 * Unpack a variable int
	 */
	unpackVariableInt: function(buffer, signed) {		
		var bits = [];
		var positive = signed ? null : true;
		while (true) {
			var b = buffer.read();
			var ba = this._byteToBits(b);

			for (var i=1; i<ba.length; i++) {
				if (positive === null) {
					positive = !ba[i];
				} else {
					bits.push(ba[i]);
				}
			}

			if (!ba[0]) {
				break;
			}
		}

		var n = 0;
		for (var i=0; i<bits.length; i++) {
			if (bits[i]) {
				n += this._powerOf2(i);
			}
		}
		return positive ? n : -n;
	},
	/**
	 * Unpack an object
	 */
	unpackObject: function(buffer) {
		var len = this.unpackVariableInt(buffer, false);
		var obj = {};
		for (var i=0; i<len; i++) {
			var key = this.unpackString(buffer);
			var value = this.unpackValue(buffer);
			obj[key] = value;
		}
		return obj;
	},
	/**
	 * Unpack an array
	 */
	unpackArray: function(buffer) {
		var len = this.unpackVariableInt(buffer, false);
		var arr = [];
		for (var i=0; i<len; i++) {
			arr.push(this.unpackValue(buffer));
		}
		return arr;
	},
	/**
	 * Unpack a string
	 */
	unpackString: function(buffer) {
		var len = this.unpackVariableInt(buffer, false);
		var bytes = buffer.read(len);
		for (var i=0; i<bytes.length; i++) {
			bytes[i] = String.fromCharCode(bytes[i]);
		}
		return decodeURIComponent(escape(
			bytes.join('')
		));
	},
	/**
	 * Unpack a byte[]
	 */
	unpackBinary: function(buffer) {
		var len = this.unpackVariableInt(buffer, false);
		return buffer.read(len);
	},
	/**
	 * Unpack a value
	 */
	unpackValue: function(buffer) {
		var type = buffer.read();
		switch(type) {
		// null
		default:
		case 0x00: return null; // Null (0)
		case 0x01: return true; // True (0)
		case 0x02: return false; // False (0)
		// Basic types
		case 0x03: return this.unpackByte(buffer); // Byte (1)
		case 0x04: return this.unpackChar(buffer); // Char (2)
		case 0x05: return this.unpackDateTime(buffer); // DateTime (8)
		case 0x06: throw "Not Implemented"; // Decimal (16)
		case 0x07: throw "Not Implemented"; // Double (8)
		case 0x08: throw "Not Implemented"; // Int8 (1)
		case 0x09: throw "Not Implemented"; // Int16 (2)
		case 0x0A: throw "Not Implemented"; // Int32 (4)
		case 0x0B: throw "Not Implemented"; // Int64 (8)
		case 0x0C: throw "Not Implemented"; // Single (4)
		case 0x0D: throw "Not Implemented"; // UInt16 (2)
		case 0x0E: throw "Not Implemented"; // UInt32 (4)
		case 0x0F: throw "Not Implemented"; // UInt64 (8)
		// - Custom
		case 0x10: return this.unpackFixed(32, buffer); // Fixed 32 (4)
		case 0x11: return this.unpackFixed(64, buffer); // Fixed 64 (8)
		case 0x12: return this.unpackVariableInt(buffer, false); // Variable UInt (1+)
		case 0x13: return this.unpackVariableInt(buffer, true); // Variable Int (1+)
		// - Complex
		case 0x20: return this.unpackObject(buffer); // object (1+)
		case 0x21: return this.unpackArray(buffer); // array  (1+)
		case 0x22: return this.unpackString(buffer); // string (1+)
		case 0x23: return this.unpackBinary(buffer); // binary (1+)
		}
	},
	/**
	 * Unpack data encoded as Packed JSON. Data can be an array of bytes, or a base64 encoded string.
	 */
	unpack: function(data, filler) {
		if (data.charCodeAt) {
			data = atob(data).split('');
			for (var i=0; i<data.length; i++) {
				data[i] = data[i].charCodeAt(0);
			}
		}
		if (filler) {
			return filler.call(this, new BinaryBuffer(data));
		}
		return this.unpackValue(new BinaryBuffer(data));
	}
};

var BinaryBuffer = function(data) {
	this._buffer = data;
	this._pos = 0;
};
BinaryBuffer.prototype = {
	read: function(n) {
		if (!n) {
			return this._buffer[this._pos++];
		} else {
			var arr = [];
			for (var i=0; i<n; i++) {
				arr.push(this.read());
			}
			return arr;
		}
	},
	peek: function() {
		return this._buffer[this._pos];
	}
};